# ClipVault - project notes

Windows-native clipboard history manager (WPF, .NET 9). Read `README.md` first for the user-facing feature list, keyboard map, and build steps. This file is the hand-off context for continuing development on any machine.

## Status (as of 2026-09-10)

Version 1.0.0. Feature-complete for the original brief and verified end to end on Windows 11:

- Capture of text (+RTF/HTML), images (saved as PNG), and file lists via `WM_CLIPBOARDUPDATE`.
- Dedup by SHA-256 hash: re-copying or reusing an item moves it to the top instead of duplicating.
- Global hotkey `Ctrl+Shift+V` (configurable), tray icon with menu, single-instance behavior.
- Popup window: search, list with per-kind templates, preview pane, pin/delete/clear, keyboard driven.
- Reuse pastes into the previously focused app (`SetForegroundWindow` + `SendInput` Ctrl+V).
- Settings dialog (hotkey capture, max items, auto-paste, capture toggles, start with Windows).
- `dotnet publish -c Release` produces one self-contained `dist\ClipVault.exe` (~69 MB, compressed single file). No installer; nothing to install.

Not done / ideas for later (none were requested, listed so nothing is forgotten):

- Code signing (unsigned exe triggers SmartScreen "More info > Run anyway" on first run).
- Inno Setup installer if a Start Menu entry / uninstaller is ever wanted (winget is available on the dev box; `iscc` was not installed).
- Dark theme (currently a fixed light palette in `App.xaml`).
- Excluding specific apps from capture, per-item "paste as plain text".
- Optional image size cap (large screenshots are stored at full resolution).

## Decisions taken with the user

Asked via a short questionnaire at the start; answers:

| Question | Answer |
| --- | --- |
| Content types | Text + images + files |
| Hotkey | Ctrl+Shift+V (avoids the built-in Win+V panel) |
| Distribution | Single self-contained exe, no installer |
| Location | `C:\Users\overona\source\repos\ClipVault` (user asked for `repos` inside `source`) |

Other choices made along the way:

- WPF over WinUI 3 / Electron: native look, no runtime dependency in a self-contained publish, easy Win32 interop.
- `UseWindowsForms=true` alongside WPF only for `NotifyIcon` (tray), `Screen`, and `Cursor.Position`. `ImplicitUsings` is disabled to avoid WPF/WinForms name clashes; every file uses explicit `using`s and fully-qualified `System.Windows.MessageBox` / `System.Windows.Clipboard`.
- No NuGet packages at all. Storage is `System.Text.Json` (history.json + images folder), not SQLite, to keep the build dependency-free.
- Priority when several formats are on the clipboard: files, then text, then image. Excel/browsers put text and a bitmap together; the text is what the user meant.
- Honors `ExcludeClipboardContentFromMonitorProcessing` and `CanIncludeInClipboardHistory=0` formats (password managers).
- Window is `WindowStyle=None` + `WindowChrome` (custom title bar with settings/close), `Topmost`, hides on `Deactivated`. Closing the window only hides it; the tray menu's Exit quits.
- DPI: `ApplicationHighDpiMode=PerMonitorV2` in the csproj (the WinForms analyzer complains if it is put in `app.manifest` instead).

## Code map

```
ClipVault.csproj            net9.0-windows, WPF + WinForms, single-file publish settings
app.manifest                asInvoker, Win10+ compat (no DPI section, see above)
App.xaml / App.xaml.cs      styles + palette; startup, single-instance mutex/event, tray icon, logging
Models/ClipItem.cs          persisted item + display helpers (Preview, TimeAgo, Thumbnail, Matches)
Services/ClipboardService.cs  Capture() clipboard -> ClipItem, Apply() ClipItem -> clipboard, retries
Services/HistoryStore.cs    ObservableCollection + JSON persistence, AddOrTouch (dedup), pin, trim
Services/Settings.cs        settings.json model, hotkey formatting, StartupRegistration (HKCU Run)
Native/NativeMethods.cs     clipboard listener, RegisterHotKey, SendInput Ctrl+V, foreground helpers
Views/MainWindow.xaml(.cs)  popup UI, WndProc hook, capture debounce, keyboard handling, paste flow
Views/SettingsWindow.xaml(.cs)  settings dialog with hotkey capture box
Views/ClipTemplateSelector.cs   picks row/preview DataTemplate by ClipKind
Assets/make-icon.ps1        draws the icon with System.Drawing and writes a multi-size .ico
tools/                      test harness scripts (see Testing)
build.ps1                   publish to dist\ (add -Zip for a shareable zip)
```

Key flow for "reuse an item": `MainWindow.UseItem` sets `_expectedHash`, calls `ClipboardService.Apply`, `HistoryStore.Touch` (moves to top, UseCount++), hides, then on a background task `SetForegroundWindow(previous)` + `SendCtrlV()`. The resulting `WM_CLIPBOARDUPDATE` sees `_expectedHash` and skips re-reading.

Second-instance flow: new process finds the mutex taken, calls `AllowSetForegroundWindow(existing pid)`, sets the named event, exits. The first instance's waiter thread calls `ShowPopup()`.

## Testing

There is no unit test project. Verification is done by driving the real app:

- `tools\e2e-test.ps1` starts the app, pushes text/image/file content onto the clipboard, opens the popup from a small WinForms form (acting as the "previous app"), types a filter, presses Enter, and asserts the text was pasted into the form's text box. It also saves a screenshot of the popup to `tools\popup.png`. Run it from a normal PowerShell window (it needs a desktop session). Pass `-Exe` to point at the published exe instead of the Debug build.
- `tools\capture-window.ps1` screenshots the running popup (DPI-aware `CopyFromScreen`; `PrintWindow` returns black for this WPF window).
- Runtime errors are appended to `%LocalAppData%\ClipVault\clipvault.log`.

Gotchas discovered while testing:

- PowerShell 5.1 is not DPI-aware; window coordinates are virtualized. Any script that captures the screen must call `SetProcessDpiAwarenessContext(-4)` first (both harness scripts do).
- The popup hides as soon as another window takes focus, so screenshots must be taken within the same script run that opened it.
- Claude Code's computer-use bridge cannot be granted access to ClipVault because it is not a Start-Menu app; that is why the PowerShell harness exists.

## Continuing on another machine

1. Clone/copy this folder. Install the .NET 9 SDK.
2. `dotnet build` for Debug, `.\build.ps1` for the shareable exe.
3. Run `tools\e2e-test.ps1` to confirm the environment works before changing anything.
