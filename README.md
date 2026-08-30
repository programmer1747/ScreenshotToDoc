# ScreenshotToDoc

Take a screenshot on one monitor and it lands in your document on another automatically!

Press **RUN**, then screenshot as you normally would. The app moves the cursor to
the screen and spot you picked, clicks to focus the document, and pastes. Take ten
screenshots in a row and all ten land in the doc without you touching the mouse.

Built for the case where your reference material is on one monitor and the doc
you're pasting into is on another.

## Download

Grab `ScreenshotToDoc.exe` from the [latest release](../../releases/latest) and run it.

No installer, no runtime to download, nothing to configure first. It's a single
31 KB executable that runs on .NET Framework 4.x, which is already part of
Windows. Windows SmartScreen will likely warn you the first time because the
binary isn't code-signed — choose *More info* then *Run anyway*, or build it
yourself from source (see below).

## Using it

1. Pick the screen your document is on from the dropdown.
2. Set where on that screen to click, or press **Pick point**, move your mouse to
   the exact spot in your doc, and it captures the position after a 5-second
   countdown.
3. Press **RUN**.
4. Screenshot anything, anywhere. Repeat as much as you like.
5. Press **STOP**, or hit `Ctrl+Alt+Q`.

**Test once** runs the whole sequence immediately using whatever is already on
your clipboard, so you can confirm the target is right before arming it.

### Choosing a screen

The dropdown lists every connected monitor with its size and position, so a
three-monitor setup reads something like:

```
Screen 1  -  1920 x 1080  at 0,0   (primary)
Screen 2  -  1080 x 1920  at -3640,-464
Screen 3  -  2560 x 1440  at -2560,-365
```

Switch the target screen any time — the paste point is stored as a *percentage*
of the chosen screen rather than as absolute pixels, so it lands in the same
relative place no matter which monitor you point it at. That also means it
survives a resolution change. If you plug or unplug a monitor while the app is
open, the list refreshes itself.

### Paste everything you copy, not just screenshots

Tick **Also paste anything I copy with Ctrl+C** and the app stops caring whether
the clipboard holds an image. Copy a paragraph of text, `Ctrl+C` a URL, grab a
snippet of code — all of it flows into the doc the same way.

One thing to watch: in this mode, copying *inside the destination document*
also triggers a paste, because the app can't tell where a copy came from. If
you're collecting from a browser into a doc you never copy from, it's seamless.
If you copy in both places, leave it off and use screenshots.

Leave it unticked and only images trigger it, which is the safer default.

### Where did the window go?

Minimising keeps the window on the taskbar, like any other app. Only if you
tick **Minimise to the system tray** does it go to the tray instead. Windows 11 hides new tray icons
behind the **^** arrow next to the clock, so click that, then double-click the
ScreenshotToDoc icon to bring the window back. A notification tells you this the
first time it happens.

The tray icon stays visible the whole time the macro is armed, so there's always
a sign it's live. `Ctrl+Alt+R` stops it from anywhere, whether you can find the
window or not. This option is off by default.

### Settings

| Setting | What it does |
| --- | --- |
| Screen | Which monitor holds the document |
| Across / Down | Where on that screen to click, as a percentage. Defaults to 50% / 80% — centred, near the bottom |
| Enter presses after each paste | How many times to press Enter afterwards, 0 for none. Applies to screenshots and copied text alike, so successive pastes stack instead of colliding |
| Send the cursor back | Returns the mouse to where it was before the paste |
| Also paste anything I copy | Fires on any `Ctrl+C`, not just images |
| Minimise to the system tray | Off by default: minimising keeps the window on the taskbar. Tick it to send the window to the tray instead |

Settings are saved to `%APPDATA%\ScreenshotToDoc\settings.json` and reload on
next launch.

### Keyboard shortcuts

These are global — they work even when the window isn't focused.

| Shortcut | Action |
| --- | --- |
| `Ctrl+Alt+D` | Start / stop watching |
| `Ctrl+Alt+Q` | Emergency stop |

Other apps often squat on these combos — capture tools, streaming overlays,
launchers and peripheral suites are common culprits, and `Ctrl+Alt+R` in
particular is frequently taken.

Rather than failing silently, the app tries `R`, `D`, `G`, `B`, `M` for
start/stop and `Q`, `W`, `H`, `J`, `N` for the emergency stop, keeps the first
combo that actually registers, and **shows the live shortcuts along the bottom
of the window**. Trust what the window says over what this table says — if
something else already owns `Ctrl+Alt+R`, you will see `Ctrl+Alt+D` there
instead.

## What counts as "a screenshot"

Anything that puts an image on the clipboard:

- `Win+Shift+S` (Snipping Tool)
- `PrtScn`
- `Alt+PrtScn` (active window)
- Copying an image from a browser or another app

It does **not** trigger on `Win+PrtScn` alone if that only writes a file to your
Screenshots folder. Snipping Tool's *Automatically copy changes to clipboard*
setting is on by default and covers this; if you've turned it off, use
`Win+Shift+S` instead.

The app listens for real clipboard-change events rather than polling on a timer,
so it reacts immediately and costs nothing while idle.

## Building from source

Requires nothing but Windows.

```
build.cmd
```

That generates the icon and compiles `src\ScreenshotToDoc.cs` with the C#
compiler bundled in `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319`, producing
`dist\ScreenshotToDoc.exe`.

Because it builds with that compiler, the source has to stay **C# 5 compatible** —
no string interpolation, no `?.`, no expression-bodied members, no `out var`.

## How it works

`AddClipboardFormatListener` subscribes the window to `WM_CLIPBOARDUPDATE`. When
one arrives and the clipboard holds something worth pasting, the app calls
`SetCursorPos` to the target point, synthesises a left click via `mouse_event`,
and sends `Ctrl+V` via `keybd_event`. Short sleeps between the steps give the
target app time to take focus before the keystrokes land.

A two-second cooldown after each paste stops a clipboard-rewriting app from
triggering a loop.

## Limitations

- Windows only.
- It drives the real mouse and keyboard, so it will type into whatever is under
  the paste point. Use **Test once** to confirm your target before arming it, and
  keep the app window off that spot.
- It cannot paste into an app running elevated (as administrator) unless it is
  elevated too — that's a Windows security boundary, not a bug.
- Only one instance runs at a time.

## License

MIT — see [LICENSE](LICENSE).
