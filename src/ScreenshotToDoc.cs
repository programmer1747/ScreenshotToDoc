// ScreenshotToDoc - take a screenshot anywhere, and it lands in your doc on
// another monitor automatically.
//
// Press RUN, then screenshot (Win+Shift+S, PrtScn, Alt+PrtScn). The app moves
// the cursor to the screen and spot you chose, clicks to focus, and pastes.
//
// Targets .NET Framework 4.x, which ships with Windows - no runtime install.
// Built with csc.exe, so this must stay C# 5 compatible:
// no string interpolation, no ?., no expression-bodied members, no "out var".

using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("ScreenshotToDoc")]
[assembly: AssemblyProduct("ScreenshotToDoc")]
[assembly: AssemblyDescription("Screenshot on one monitor, auto-paste into a doc on another.")]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]

namespace ScreenshotToDoc
{
    internal static class Native
    {
        [DllImport("user32.dll")] internal static extern bool AddClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] internal static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);
        [DllImport("user32.dll")] internal static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
        [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT { public int X; public int Y; }

        internal const int WM_CLIPBOARDUPDATE = 0x031D;
        internal const int WM_HOTKEY = 0x0312;

        internal const uint MOD_ALT = 0x0001;
        internal const uint MOD_CONTROL = 0x0002;
        internal const uint MOD_NOREPEAT = 0x4000;

        private const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004, KEYUP = 0x0002;
        private const byte VK_CONTROL = 0x11, VK_V = 0x56, VK_RETURN = 0x0D;

        internal static void Click()
        {
            mouse_event(LEFTDOWN, 0, 0, 0, IntPtr.Zero);
            Thread.Sleep(40);
            mouse_event(LEFTUP, 0, 0, 0, IntPtr.Zero);
        }

        internal static void Paste()
        {
            keybd_event(VK_CONTROL, 0, 0, IntPtr.Zero);
            keybd_event(VK_V, 0, 0, IntPtr.Zero);
            Thread.Sleep(40);
            keybd_event(VK_V, 0, KEYUP, IntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYUP, IntPtr.Zero);
        }

        internal static void PressEnter()
        {
            keybd_event(VK_RETURN, 0, 0, IntPtr.Zero);
            Thread.Sleep(25);
            keybd_event(VK_RETURN, 0, KEYUP, IntPtr.Zero);
        }
    }

    [DataContract]
    internal class Settings
    {
        // Screen is stored by device name first so it survives monitors being
        // re-ordered in Windows display settings; index is the fallback.
        [DataMember] public string ScreenDevice = "";
        [DataMember] public int ScreenIndex = -1;

        // Paste point is a percentage of the target screen, not absolute
        // pixels, so it stays correct if that monitor's resolution changes.
        [DataMember] public double PctX = 50;
        [DataMember] public double PctY = 80;

        // Legacy flag, kept so older settings files still load.
        [DataMember] public bool PressEnter = false;

        // How many times to press Enter after a paste. -1 means "not written
        // yet", in which case the old boolean decides.
        [DataMember] public int EnterCount = -1;

        [DataMember] public bool ReturnCursor = false;

        // Off by default: turning this on makes every Ctrl+C paste, which is
        // powerful but surprising if you did not ask for it.
        [DataMember] public bool PasteText = false;

        // Off by default so the window never vanishes on someone unprepared.
        [DataMember] public bool MinimizeOnRun = false;

        internal int EffectiveEnterCount()
        {
            if (EnterCount >= 0) return EnterCount;
            return PressEnter ? 1 : 0;
        }

        private static string ConfigDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ScreenshotToDoc");
            }
        }

        private static string ConfigPath
        {
            get { return Path.Combine(ConfigDir, "settings.json"); }
        }

        internal static Settings Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    using (FileStream fs = File.OpenRead(ConfigPath))
                    {
                        DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(Settings));
                        Settings s = ser.ReadObject(fs) as Settings;
                        if (s != null) return s;
                    }
                }
            }
            catch { }
            return new Settings();
        }

        internal void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                using (FileStream fs = File.Create(ConfigPath))
                {
                    DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(Settings));
                    ser.WriteObject(fs, this);
                }
            }
            catch { }
        }
    }

    internal class MainForm : Form
    {
        private const int HK_STOP = 1;
        private const int HK_TOGGLE = 2;

        private readonly Settings cfg = Settings.Load();
        private bool running;
        private int pasteCount;
        private DateTime coolUntil = DateTime.MinValue;
        private int pickLeft;

        private ComboBox cboScreen;
        private NumericUpDown numX, numY, numEnter;
        private Label lblAbs, lblStatus, lblHint;

        // Whichever combos actually registered, for display.
        private string toggleHotkey, stopHotkey;

        // Other apps routinely squat on these combos - Ctrl+Alt+R in particular
        // is popular with capture and streaming tools. Try a short list and keep
        // the first that registers rather than silently ending up with nothing.
        private static readonly Keys[] ToggleKeys = { Keys.R, Keys.D, Keys.G, Keys.B, Keys.M };
        private static readonly Keys[] StopKeys = { Keys.Q, Keys.W, Keys.H, Keys.J, Keys.N };
        private CheckBox chkReturn, chkText, chkMinimize;
        private Button btnRun, btnTest, btnPick;
        private NotifyIcon tray;
        private System.Windows.Forms.Timer pickTimer;

        internal MainForm()
        {
            BuildUi();
            PopulateScreens();
            numX.Value = ClampPct(cfg.PctX);
            numY.Value = ClampPct(cfg.PctY);
            int enters = cfg.EffectiveEnterCount();
            if (enters < 0) enters = 0;
            if (enters > 20) enters = 20;
            numEnter.Value = enters;
            chkReturn.Checked = cfg.ReturnCursor;
            chkText.Checked = cfg.PasteText;
            chkMinimize.Checked = cfg.MinimizeOnRun;
            UpdateReadout();

            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
        }

        private static decimal ClampPct(double v)
        {
            if (v < 0) v = 0;
            if (v > 100) v = 100;
            return (decimal)Math.Round(v, 1);
        }

        // ---------- UI ----------

        private Label AddLabel(string text, int x, int y, int w, int h)
        {
            Label l = new Label();
            l.Text = text;
            l.Location = new Point(x, y);
            l.Size = new Size(w, h);
            Controls.Add(l);
            return l;
        }

        private void BuildUi()
        {
            Text = "ScreenshotToDoc";
            ClientSize = new Size(430, 454);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            Font = new Font("Segoe UI", 9f);

            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            AddLabel("Paste into the doc on this screen:", 14, 12, 300, 18);

            cboScreen = new ComboBox();
            cboScreen.Location = new Point(14, 32);
            cboScreen.Size = new Size(402, 24);
            cboScreen.DropDownStyle = ComboBoxStyle.DropDownList;
            cboScreen.SelectedIndexChanged += delegate { UpdateReadout(); };
            Controls.Add(cboScreen);

            AddLabel("Where on that screen:", 14, 70, 200, 18);

            AddLabel("Across", 14, 96, 48, 20);
            numX = MakePct(66, 93);
            AddLabel("%", 122, 96, 15, 20);

            AddLabel("Down", 160, 96, 40, 20);
            numY = MakePct(204, 93);
            AddLabel("%", 260, 96, 15, 20);

            btnPick = new Button();
            btnPick.Text = "Pick point";
            btnPick.Location = new Point(296, 91);
            btnPick.Size = new Size(120, 27);
            btnPick.Click += OnPickClick;
            Controls.Add(btnPick);

            lblAbs = AddLabel("", 14, 126, 402, 18);
            lblAbs.ForeColor = Color.FromArgb(60, 60, 60);

            AddLabel("Enter presses after each paste:", 14, 158, 190, 20);
            numEnter = new NumericUpDown();
            numEnter.Location = new Point(206, 155);
            numEnter.Size = new Size(56, 24);
            numEnter.Minimum = 0;
            numEnter.Maximum = 20;
            Controls.Add(numEnter);
            Label enterHint = AddLabel("0 = none  (images and text)", 270, 158, 150, 20);
            enterHint.ForeColor = Color.Gray;

            chkReturn = MakeCheck("Send the cursor back where it was afterwards", 182);
            chkText = MakeCheck("Also paste anything I copy with Ctrl+C, not just screenshots", 208);
            chkMinimize = MakeCheck("Minimise to the system tray instead of the taskbar", 234);

            btnRun = new Button();
            btnRun.Text = "RUN";
            btnRun.Location = new Point(14, 270);
            btnRun.Size = new Size(250, 54);
            btnRun.Font = new Font("Segoe UI", 13f, FontStyle.Bold);
            btnRun.BackColor = Color.FromArgb(76, 175, 80);
            btnRun.ForeColor = Color.White;
            btnRun.FlatStyle = FlatStyle.Flat;
            btnRun.FlatAppearance.BorderSize = 0;
            btnRun.Click += delegate { if (running) StopWatching(); else StartWatching(); };
            Controls.Add(btnRun);

            btnTest = new Button();
            btnTest.Text = "Test once";
            btnTest.Location = new Point(276, 270);
            btnTest.Size = new Size(140, 54);
            btnTest.Click += OnTestClick;
            Controls.Add(btnTest);

            lblStatus = AddLabel("Idle. Press RUN, then take a screenshot.", 14, 338, 402, 40);
            lblStatus.Font = new Font("Segoe UI", 9f, FontStyle.Bold);

            lblHint = AddLabel("", 14, 386, 402, 56);
            lblHint.ForeColor = Color.Gray;

            tray = new NotifyIcon();
            tray.Text = "ScreenshotToDoc";
            tray.Icon = Icon != null ? Icon : SystemIcons.Application;
            tray.DoubleClick += delegate { RestoreFromTray(); };

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Open", null, delegate { RestoreFromTray(); });
            menu.Items.Add("Start / stop", null, delegate { if (running) StopWatching(); else StartWatching(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { tray.Visible = false; Close(); });
            tray.ContextMenuStrip = menu;

            pickTimer = new System.Windows.Forms.Timer();
            pickTimer.Interval = 1000;
            pickTimer.Tick += OnPickTick;
        }

        private NumericUpDown MakePct(int x, int y)
        {
            NumericUpDown n = new NumericUpDown();
            n.Location = new Point(x, y);
            n.Size = new Size(52, 24);
            n.Minimum = 0;
            n.Maximum = 100;
            n.DecimalPlaces = 1;
            n.Increment = 5;
            n.ValueChanged += delegate { UpdateReadout(); };
            Controls.Add(n);
            return n;
        }

        private CheckBox MakeCheck(string text, int y)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.Location = new Point(14, y);
            c.Size = new Size(402, 22);
            Controls.Add(c);
            return c;
        }

        // ---------- screens ----------

        private void PopulateScreens()
        {
            Screen[] all = Screen.AllScreens;
            int keep = IndexOfConfiguredScreen();

            cboScreen.BeginUpdate();
            cboScreen.Items.Clear();
            for (int i = 0; i < all.Length; i++)
            {
                Rectangle b = all[i].Bounds;
                cboScreen.Items.Add(string.Format(
                    "Screen {0}  -  {1} x {2}  at {3},{4}{5}",
                    i + 1, b.Width, b.Height, b.X, b.Y,
                    all[i].Primary ? "   (primary)" : ""));
            }
            cboScreen.EndUpdate();

            if (cboScreen.Items.Count > 0)
                cboScreen.SelectedIndex = Math.Max(0, Math.Min(keep, cboScreen.Items.Count - 1));
        }

        private int IndexOfConfiguredScreen()
        {
            Screen[] all = Screen.AllScreens;

            if (!string.IsNullOrEmpty(cfg.ScreenDevice))
                for (int i = 0; i < all.Length; i++)
                    if (all[i].DeviceName == cfg.ScreenDevice) return i;

            if (cfg.ScreenIndex >= 0 && cfg.ScreenIndex < all.Length)
                return cfg.ScreenIndex;

            // First run: a third monitor is usually the doc screen in this setup.
            if (all.Length >= 3) return 2;
            for (int i = 0; i < all.Length; i++)
                if (all[i].Primary) return i;
            return 0;
        }

        private Screen SelectedScreen()
        {
            Screen[] all = Screen.AllScreens;
            int i = cboScreen.SelectedIndex;
            if (i < 0 || i >= all.Length) i = 0;
            return all[i];
        }

        private Point TargetPoint()
        {
            Rectangle b = SelectedScreen().Bounds;
            int x = b.Left + (int)Math.Round(b.Width * (double)numX.Value / 100.0);
            int y = b.Top + (int)Math.Round(b.Height * (double)numY.Value / 100.0);
            if (x < b.Left) x = b.Left;
            if (x > b.Right - 1) x = b.Right - 1;
            if (y < b.Top) y = b.Top;
            if (y > b.Bottom - 1) y = b.Bottom - 1;
            return new Point(x, y);
        }

        private void UpdateReadout()
        {
            if (cboScreen == null || numX == null || numY == null || lblAbs == null) return;
            Point p = TargetPoint();
            lblAbs.Text = string.Format("Clicks and pastes at {0}, {1}", p.X, p.Y);
        }

        private void OnDisplaysChanged(object sender, EventArgs e)
        {
            PopulateScreens();
            UpdateReadout();
            SetStatus("Displays changed - target screen re-checked.");
        }

        // ---------- macro ----------

        private void DoPasteSequence()
        {
            Point target = TargetPoint();

            Native.POINT old;
            Native.GetCursorPos(out old);

            Native.SetCursorPos(target.X, target.Y);   // switch monitors
            Thread.Sleep(150);
            Native.Click();                            // focus the doc
            Thread.Sleep(250);
            Native.Paste();                            // Ctrl+V

            // Same path for images and text, so the Enter presses apply to both.
            int enters = (int)numEnter.Value;
            for (int i = 0; i < enters; i++)
            {
                Thread.Sleep(i == 0 ? 200 : 60);
                Native.PressEnter();
            }

            if (chkReturn.Checked) { Thread.Sleep(150); Native.SetCursorPos(old.X, old.Y); }

            pasteCount++;
            coolUntil = DateTime.Now.AddSeconds(2);
            SetStatus(string.Format("Pasted {0} screenshot(s). Last at {1}, {2}.",
                pasteCount, target.X, target.Y));
        }

        // What counts as worth pasting. Images always; text as well once the
        // user opts in, which turns any Ctrl+C into a paste.
        private static bool ClipboardHasContent(bool includeText)
        {
            // The clipboard can briefly be locked by the app that just wrote to
            // it, so a couple of retries avoids missing a screenshot.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (Clipboard.ContainsImage()) return true;
                    return includeText && Clipboard.ContainsText();
                }
                catch { Thread.Sleep(60); }
            }
            return false;
        }

        private void StartWatching()
        {
            SaveConfig();
            running = true;
            coolUntil = DateTime.Now.AddMilliseconds(400);
            btnRun.Text = "STOP";
            btnRun.BackColor = Color.FromArgb(211, 47, 47);
            cboScreen.Enabled = false;
            numX.Enabled = false;
            numY.Enabled = false;
            btnPick.Enabled = false;
            tray.Text = "ScreenshotToDoc - watching";
            tray.Visible = true;   // always a visible sign that it is armed
            SetStatus(chkText.Checked
                ? "Watching. Screenshot or copy anything."
                : "Watching. Take a screenshot.");
            if (chkMinimize.Checked) WindowState = FormWindowState.Minimized;
        }

        private void StopWatching()
        {
            running = false;
            btnRun.Text = "RUN";
            btnRun.BackColor = Color.FromArgb(76, 175, 80);
            cboScreen.Enabled = true;
            numX.Enabled = true;
            numY.Enabled = true;
            btnPick.Enabled = true;
            tray.Text = "ScreenshotToDoc";
            SetStatus(string.Format("Stopped. Pasted {0} this session.", pasteCount));
            RestoreFromTray();
        }

        private void SetStatus(string text)
        {
            lblStatus.Text = text;
        }

        private void SaveConfig()
        {
            cfg.ScreenDevice = SelectedScreen().DeviceName;
            cfg.ScreenIndex = cboScreen.SelectedIndex;
            cfg.PctX = (double)numX.Value;
            cfg.PctY = (double)numY.Value;
            cfg.EnterCount = (int)numEnter.Value;
            cfg.PressEnter = cfg.EnterCount > 0;   // keep the legacy flag in step
            cfg.ReturnCursor = chkReturn.Checked;
            cfg.PasteText = chkText.Checked;
            cfg.MinimizeOnRun = chkMinimize.Checked;
            cfg.Save();
        }

        // ---------- events ----------

        private void OnPickClick(object sender, EventArgs e)
        {
            pickLeft = 6;
            btnRun.Enabled = false;
            btnTest.Enabled = false;
            btnPick.Enabled = false;
            SetStatus("Move the mouse to the spot in your doc... 5");
            pickTimer.Start();
        }

        private void OnPickTick(object sender, EventArgs e)
        {
            pickLeft--;
            if (pickLeft > 0)
            {
                SetStatus(string.Format("Move the mouse to the spot in your doc... {0}", pickLeft));
                return;
            }
            pickTimer.Stop();

            Native.POINT p;
            Native.GetCursorPos(out p);

            Screen[] all = Screen.AllScreens;
            bool found = false;
            for (int i = 0; i < all.Length && !found; i++)
            {
                Rectangle b = all[i].Bounds;
                if (!b.Contains(p.X, p.Y)) continue;
                cboScreen.SelectedIndex = i;
                numX.Value = ClampPct((p.X - b.Left) * 100.0 / b.Width);
                numY.Value = ClampPct((p.Y - b.Top) * 100.0 / b.Height);
                SetStatus(string.Format("Set to Screen {0} at {1}, {2}.", i + 1, p.X, p.Y));
                found = true;
            }
            if (!found) SetStatus("That point is not on any screen - try again.");

            UpdateReadout();
            SaveConfig();
            btnRun.Enabled = true;
            btnTest.Enabled = true;
            btnPick.Enabled = true;
        }

        private void OnTestClick(object sender, EventArgs e)
        {
            if (!ClipboardHasContent(chkText.Checked))
            {
                SetStatus(chkText.Checked
                    ? "Nothing to test - copy something first."
                    : "Nothing to test - copy a screenshot first.");
                return;
            }
            SaveConfig();
            DoPasteSequence();
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            // Keep the tray icon while it is still armed, so there is always a
            // visible sign that the macro is live.
            tray.Visible = running;
            Activate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState != FormWindowState.Minimized) return;

            // Only disappear into the tray when explicitly asked to. Without
            // this check, every minimise hid the window whether the option was
            // ticked or not, which reads exactly like the app closing itself.
            if (chkMinimize == null || !chkMinimize.Checked) return;

            // Windows 11 tucks newly added tray icons behind the chevron, so a
            // window that just silently vanished is genuinely hard to find.
            // Say where it went instead of leaving the user hunting.
            Hide();
            tray.Visible = true;
            tray.BalloonTipTitle = "ScreenshotToDoc is still running";
            tray.BalloonTipText =
                "It is in the system tray. Click the ^ arrow next to the clock, "
                + "then double-click the icon to bring this window back. "
                + "Ctrl+Alt+R stops it from anywhere.";
            tray.BalloonTipIcon = ToolTipIcon.Info;
            try { tray.ShowBalloonTip(9000); }
            catch { }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.AddClipboardFormatListener(Handle);
            toggleHotkey = RegisterFirstAvailable(HK_TOGGLE, ToggleKeys);
            stopHotkey = RegisterFirstAvailable(HK_STOP, StopKeys);
            UpdateHotkeyHint();
        }

        // Returns the combo that registered, or null if every candidate was
        // already claimed by another application.
        private string RegisterFirstAvailable(int id, Keys[] candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                if (Native.RegisterHotKey(Handle, id,
                        Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT,
                        (uint)candidates[i]))
                {
                    return "Ctrl+Alt+" + candidates[i];
                }
            }
            return null;
        }

        private void UpdateHotkeyHint()
        {
            if (lblHint == null) return;

            string line;
            if (toggleHotkey == null && stopHotkey == null)
                line = "No global hotkey available - other apps hold them all.";
            else if (toggleHotkey == null)
                line = stopHotkey + "  emergency stop      (start/stop combo is taken)";
            else if (stopHotkey == null)
                line = toggleHotkey + "  start / stop      (stop combo is taken)";
            else
                line = toggleHotkey + "  start / stop      " + stopHotkey + "  emergency stop";

            lblHint.Text = line + "\r\n"
                + "Hiding to the tray? Click the ^ arrow by the clock to find it.";
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_CLIPBOARDUPDATE)
            {
                if (running && DateTime.Now >= coolUntil && ClipboardHasContent(chkText.Checked))
                    DoPasteSequence();
            }
            else if (m.Msg == Native.WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HK_STOP)
                {
                    if (running) StopWatching();
                }
                else if (id == HK_TOGGLE)
                {
                    if (running) StopWatching(); else StartWatching();
                }
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
            Native.RemoveClipboardFormatListener(Handle);
            Native.UnregisterHotKey(Handle, HK_STOP);
            Native.UnregisterHotKey(Handle, HK_TOGGLE);
            pickTimer.Stop();
            SaveConfig();
            tray.Visible = false;
            base.OnFormClosing(e);
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool isFirstInstance;
            using (Mutex mutex = new Mutex(true, "ScreenshotToDoc_SingleInstance", out isFirstInstance))
            {
                if (!isFirstInstance)
                {
                    MessageBox.Show("ScreenshotToDoc is already running.",
                        "ScreenshotToDoc", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Native.SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());

                GC.KeepAlive(mutex);
            }
        }
    }
}
