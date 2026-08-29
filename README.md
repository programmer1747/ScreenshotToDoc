# ScreenshotToDoc

Take a screenshot on one monitor and it lands in your document on another — automatically.

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

**Test once** runs the whole sequence immediately using whatever image is already
on your clipboard, so you can confirm the target is right before arming it.

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

### Settings

| Setting | What it does |
| --- | --- |
| Screen | Which monitor holds the document |
| Across / Down | Where on that screen to click, as a percentage. Defaults to 50% / 80% — centred, near the bottom |
| Press Enter after pasting | Adds a newline so successive screenshots stack instead of colliding |
| Send the cursor back | Returns the mouse to where it was before the paste |
| Minimise to the tray | Gets the window out of the way while it's watching |

Settings are saved to `%APPDATA%\ScreenshotToDoc\settings.json` and reload on
next launch.

### Keyboard shortcuts

These are global — they work even when the window isn't focused.

| Shortcut | Action |
| --- | --- |
| `Ctrl+Alt+R` | Start / stop watching |
| `Ctrl+Alt+Q` | Emergency stop |

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
one arrives and the clipboard holds an image, the app calls `SetCursorPos` to the
target point, synthesises a left click via `mouse_event`, and sends `Ctrl+V` via
`keybd_event`. Short sleeps between the steps give the target app time to take
focus before the keystrokes land.

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
