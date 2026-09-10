# ClipVault - project notes

Windows-native clipboard history manager (WPF, .NET 9). Read `README.md` first for the user-facing feature list, keyboard map, and build steps. This file is the hand-off context for continuing development on any machine.

## Status (as of 2026-09-10)

Version 1.1.0. Feature-complete for the original brief and verified end to end on Windows 11:

- Capture of text (+RTF/HTML), images (saved as PNG), and file lists via `WM_CLIPBOARDUPDATE`.
- Dedup by SHA-256 hash: re-copying or reusing an item moves it to the top instead of duplicating.
- Global hotkey `Ctrl+Shift+V` (configurable), tray icon with menu, single-instance behavior.
- Popup window: search, list with per-kind templates, preview pane, pin/delete/clear, keyboard driven.
- Reuse pastes into the previously focused app (`SetForegroundWindow` + `SendInput` Ctrl+V).
- Settings dialog (hotkey capture, max items, auto-paste, capture toggles, start with Windows).
- `dotnet publish -c Release` produces one self-contained `dist\ClipVault.exe` (~66 MB, compressed single file). No separate installer: the exe installs itself on first run (see 1.1.0 notes).

Added in 1.1.0 (second session, same machine):

- Paste as plain text: `Shift+Enter` / context menu strips RTF+HTML; `Ctrl+Shift+Enter` copies plain only. `ClipboardService.Apply(item, plainText)`.
- Light/dark theme: `Services/ThemeManager.cs` swaps the palette brushes in `Application.Resources`; every palette reference in XAML is `DynamicResource`. Setting `Theme` = System (reads `AppsUseLightTheme`, follows `SystemEvents.UserPreferenceChanged`) / Light / Dark. Settings dialog gets a dark title bar via `DwmSetWindowAttribute(20)`. Message boxes and the tray menu stay system-styled.
- Excluded apps: `Settings.ExcludedApps` (process names, `.exe` optional); `MainWindow.CaptureClipboard` checks the foreground process before reading the clipboard.
- Image size cap: `Settings.MaxImageEdge` (default 4096, 0 = off) scales down via `TransformedBitmap` before hashing/saving, so the hash is of the stored PNG.
- Code formatting: `Services/CodeDetector.cs` is a line-based heuristic (indentation, braces/semicolons, keyword/command prefixes, JSON/YAML keys, tags, a prose check). `ClipItem.IsCode` caches it; `ClipTemplateSelector.Code` picks the `CodeRow` / `CodePreview` templates (monospace, `CodeBg` brush, no wrapping). Sample cases live only in this session's notes; to re-test, compile the file into a scratch console app (see the 2026-09-10 session).
- List rows are `Focusable=False` so typing stays in the search box, which stops WPF from selecting a row on click; `MainWindow.ItemList_PreviewMouseDown` selects the clicked row explicitly (bug in 1.0).
- Self-install for sharing: `Services/Installer.cs` + `Views/InstallWindow`. On startup (after settings + theme, before anything else) `Installer.ShouldOffer` is true when the exe is not `%LocalAppData%\Programs\ClipVault\ClipVault.exe`, the user has not dismissed the prompt for this exact path (`Settings.InstallPromptDismissedFor`), and there is no installed copy or it is older (`FileVersionInfo` vs assembly version). Install = copy exe, HKCU Run entry (opt-in), Start menu `.lnk` via `WScript.Shell` COM, then release the mutex/event handles, launch the installed copy, exit. Update path works even when an instance is running: `Local\ClipVault.Exit` event asks it to `ExitApp` (saves history), `WaitForOtherInstancesToExit` waits then kills. `--portable` skips the prompt (both harness scripts pass it). Tray shows "Install..." or "Uninstall..." depending on `IsInstalledCopy`; uninstall schedules the exe and folder for deletion at next reboot via `MoveFileEx(MOVEFILE_DELAY_UNTIL_REBOOT)` (a spawned delete script looked like malware to behaviour monitoring). `StartupRegistration.RepairIfStale` re-points a Run entry whose target is gone. Nothing needs admin: user Programs folder, HKCU, user Start menu.
- About dialog (`Views/AboutWindow`): version + installed/portable, author "Ovidio Verona" (`AboutWindow.Author`), copyright, disclaimer text, "Open data folder". Reached from the tray menu and the Settings dialog. The csproj sets `Authors`/`Company`/`Copyright` to the author's personal name so the exe's file properties match; keep it a person, not a company name.
- Clickable preview (1.1.3): `Views/LinkTextBox.cs` is a read-only RichTextBox (`IsDocumentEnabled=True` so a plain click follows a Hyperlink) that rebuilds a FlowDocument from its `Text` DP, linking URLs, e-mails and paths that exist on disk (`File.Exists`/`Directory.Exists`, so prose with backslashes is left alone); clips over 200k chars are shown plain. Code clips keep the monospace TextBox. `FilesPreview` rows are Hyperlinks plus a "show in folder" button; `Services/LinkOpener.cs` does `Process.Start(UseShellExecute)` and `explorer /select`. Hyperlink style (accent, underline on hover) lives in App.xaml.
- `App.xaml` now also carries theme-aware implicit styles for TextBox, CheckBox, RadioButton, ToolTip, ContextMenu/MenuItem and a slim ScrollBar.

Not done / ideas for later (none were requested, listed so nothing is forgotten):

- Inno Setup installer if a Start Menu entry / uninstaller is ever wanted (winget is available on the dev box; `iscc` was not installed).
- Dark styling for `MessageBox` (still system light) and the WinForms tray menu.
- Excluded-apps check uses the foreground process at capture time; apps that copy in the background are not matched.

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
- 1.1: window is also `AllowsTransparency=True` with a transparent background and an 8px-rounded root Border, so `Opacity` can be animated. `PositionOnCursorScreen` docks it against the taskbar edge (bottom/top/left/right, detected from `Screen.Bounds` vs `WorkingArea`) centered along that edge, and `SlideIn` animates `Left`/`Top` 56 DIPs from that edge plus a 160 ms fade (`FillBehavior.Stop`, base values already final). Hide is still instant so the paste flow is not delayed.
- Preview toolbar buttons are all icon buttons (paste E77F in accent, copy E8C8, pin, delete). Each list row header has a hover/selected-only copy icon (`RowCopy_Click`); `ItemList_MouseDoubleClick` ignores double-clicks that land on a button.
- DPI: `ApplicationHighDpiMode=PerMonitorV2` in the csproj (the WinForms analyzer complains if it is put in `app.manifest` instead).

## Code map

```
ClipVault.csproj            net9.0-windows, WPF + WinForms, single-file publish settings
app.manifest                asInvoker, Win10+ compat (no DPI section, see above)
App.xaml / App.xaml.cs      styles + palette; startup, single-instance mutex/event, tray icon, logging
Models/ClipItem.cs          persisted item + display helpers (Preview, TimeAgo, Thumbnail, Matches)
Services/ClipboardService.cs  Capture() clipboard -> ClipItem, Apply() ClipItem -> clipboard, retries
Services/HistoryStore.cs    ObservableCollection + JSON persistence, AddOrTouch (dedup), pin, trim
Services/Settings.cs        settings.json model, hotkey formatting, theme/excluded apps/image cap, StartupRegistration (HKCU Run)
Services/ThemeManager.cs    light/dark palette swap, reads Windows app mode
Services/Installer.cs       self-install/update/uninstall into %LocalAppData%\Programs\ClipVault
Views/InstallWindow.xaml(.cs)   first-run "Install / Run from here" and "Update / Not now" dialog
Services/CodeDetector.cs    heuristic "is this text code?" used for monospace rendering
Native/NativeMethods.cs     clipboard listener, RegisterHotKey, SendInput Ctrl+V, foreground helpers
Views/MainWindow.xaml(.cs)  popup UI, WndProc hook, capture debounce, keyboard handling, paste flow
Views/SettingsWindow.xaml(.cs)  settings dialog with hotkey capture box
Views/AboutWindow.xaml(.cs)     about box: version, author, disclaimer
Views/LinkTextBox.cs            read-only RichTextBox that linkifies URLs/e-mails/existing paths
Services/LinkOpener.cs      opens links, files, folders via the shell
Views/ClipTemplateSelector.cs   picks row/preview DataTemplate by ClipKind
Assets/make-icon.ps1        draws the icon with System.Drawing and writes a multi-size .ico
tools/                      test harness scripts (see Testing)
build.ps1                   publish to dist\ (add -Zip for a shareable zip)
```

Key flow for "reuse an item": `MainWindow.UseItem` sets `_expectedHash`, calls `ClipboardService.Apply`, `HistoryStore.Touch` (moves to top, UseCount++), hides, then on a background task `SetForegroundWindow(previous)` + `SendCtrlV()`. The resulting `WM_CLIPBOARDUPDATE` sees `_expectedHash` and skips re-reading.

Second-instance flow: new process finds the mutex taken, calls `AllowSetForegroundWindow(existing pid)`, sets the named event, exits. The first instance's waiter thread calls `ShowPopup()`.

## Releasing

Public repo: https://github.com/overona/ClipVault (SSH remote `origin`, branch `main`). `.github/workflows/build.yml` compiles on every push/PR; `.github/workflows/release.yml` runs on a `v*` tag, checks the tag equals `<Version>` in the csproj, runs `build.ps1 -Zip`, writes `SHA256SUMS.txt`, and creates the GitHub Release with the exe, zip and checksums via `gh` (GITHUB_TOKEN, `contents: write`). To ship: bump `<Version>` in `ClipVault.csproj` and `version=` in `app.manifest`, commit, `git tag vX.Y.Z`, `git push origin main vX.Y.Z`. Code signing (set up 2026-09-10, Azure Artifact Signing, formerly "Trusted Signing"): account `veronasolutions` (Basic, East US, RG `VSGroup`, endpoint https://eus.codesigning.azure.net/). GitHub Actions signs via OIDC: Entra app `github-clipvault-signing` (client b1023cc3-846e-4497-88bd-f40fd0c2e051) has a federated credential whose subject must be the ID-qualified form GitHub now presents, `repo:overona@8826470/ClipVault@1364707993:environment:release` (the classic `repo:overona/ClipVault:environment:release` fails with AADSTS700213; the error message shows the exact presented subject), so the release job declares `environment: release`; the IDs are plain values in release.yml, no secrets. Roles on the account: the app is "Artifact Signing Certificate Profile Signer", the user is "Artifact Signing Identity Verifier". Signing + login are `continue-on-error`, so a release ships unsigned (with the SmartScreen note) until the certificate profile `veronasolutions` exists. To finish: complete identity validation in the portal, then `az trustedsigning certificate-profile create --account-name veronasolutions -n veronasolutions -g VSGroup --profile-type PublicTrust --identity-validation-id <id>` (extension `trustedsigning`; run az from PowerShell, Git Bash mangles the `/subscriptions/...` scope).

## Testing

There is no unit test project. Verification is done by driving the real app:

- `tools\e2e-test.ps1` starts the app, pushes text/image/file content onto the clipboard, opens the popup from a small WinForms form (acting as the "previous app"), types a filter, presses Enter, and asserts the text was pasted into the form's text box. A second round does the same with `Shift+Enter` (plain text) and expects UseCount 2. It also saves a screenshot of the popup to `tools\popup.png`. Run it from a normal PowerShell window (it needs a desktop session). Pass `-Exe` to point at the published exe instead of the Debug build.
- `tools\promo-shots.ps1` produces the two site screenshots (light + dark) into `..\VSssets\promo\clipvault`: restarts the installed exe, seeds link text / code / image / files, flips `Theme` in settings.json between shots, restores it. `capture-window.ps1 -Keys "{DOWN}"` sends keys before the shot (used to step past a pinned item).
- `tools\capture-window.ps1` screenshots the running popup (DPI-aware `CopyFromScreen`; `PrintWindow` returns black for this WPF window).
- Runtime errors are appended to `%LocalAppData%\ClipVault\clipvault.log`.

Gotchas discovered while testing:

- PowerShell 5.1 is not DPI-aware; window coordinates are virtualized. Any script that captures the screen must call `SetProcessDpiAwarenessContext(-4)` first (both harness scripts do).
- The popup hides as soon as another window takes focus, so screenshots must be taken within the same script run that opened it.
- Claude Code's computer-use bridge cannot be granted access to ClipVault because it is not a Start-Menu app; that is why the PowerShell harness exists.
- If the workstation is locked (`Get-Process LogonUI` succeeds, `GetForegroundWindow` returns 0), `SendKeys` throws "Access is denied" and every assertion fails. Unlock and rerun; nothing is wrong with the app.
- Stop the running ClipVault before `dotnet build`; the Debug exe keeps `bin\Debug\...\ClipVault.dll` locked.
- `Get-Process LogonUI` is NOT a reliable lock check: LogonUI can keep running after an unlock. If a capture fails with "handle is invalid" from CopyFromScreen, the desktop really is locked; otherwise just try.

## Continuing on another machine

1. Clone/copy this folder. Install the .NET 9 SDK.
2. `dotnet build` for Debug, `.\build.ps1` for the shareable exe.
3. Run `tools\e2e-test.ps1` to confirm the environment works before changing anything.
