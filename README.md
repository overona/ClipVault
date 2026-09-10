# ClipVault

A native Windows clipboard history manager. It runs quietly in the system tray, remembers everything you copy (text, images, files), and lets you bring any of it back with a keystroke.

## Download

**[Latest release](https://github.com/overona/ClipVault/releases/latest)**: grab `ClipVault.exe`, run it, and let it install itself (no admin rights). Each release lists SHA-256 checksums in `SHA256SUMS.txt`. Full details under [Installing / sharing](#installing--sharing).

## Features

- **Global hotkey**: `Ctrl+Shift+V` opens the history from any app (changeable in Settings).
- **Content-aware views**: text shows a multi-line preview, images show a thumbnail and full-size preview, copied files show a file list.
- **Search as you type**: just start typing when the window is open.
- **Reuse without duplicates**: picking an item puts it back on the clipboard, pastes it into the app you came from, and moves it to the top of the history. Copying something you already have in history also moves the existing entry up instead of adding a second copy.
- **Pin** items you want to keep around; pinned items stay at the top and are never trimmed.
- **Rich text preserved**: RTF and HTML formats are kept alongside plain text, so pasting into Word or Outlook keeps formatting. `Shift+Enter` pastes the plain text only when you want to drop the formatting.
- **Code looks like code**: snippets, JSON, markup, SQL and shell commands are detected and shown in a monospace font with a code background, unwrapped, in both the list and the preview.
- **Clickable preview**: web links and e-mail addresses in a text item open in your browser or mail app, file paths that still exist open too, and each entry in a copied-files item can be opened or shown in Explorer.
- **Light and dark**: follows the Windows app mode by default, or pick a theme in Settings.
- **Leave some apps out**: list your password manager (or anything else) in Settings and its copies are never recorded.
- **Image size cap**: screenshots larger than a configurable size are shrunk on capture so the history folder stays small (default 4096 px on the long edge; 0 keeps originals).
- **Respects privacy flags**: content marked by password managers as "exclude from clipboard history" is never stored.
- **Single instance**: launching the exe again just opens the window of the running copy.
- Optional **start with Windows**.

## Keyboard

| Key | Action |
| --- | --- |
| `Ctrl+Shift+V` | Open / close ClipVault (global) |
| Type anything | Filter the list |
| `Up` / `Down`, `PageUp` / `PageDown` | Move selection |
| `Enter` or double-click | Paste the selected item into the previous app |
| Copy icon on a row | Copy that item to the clipboard without pasting |
| `Shift+Enter` | Paste as plain text (drops RTF/HTML formatting) |
| `Ctrl+Enter` | Copy to clipboard only, don't paste |
| `Ctrl+Shift+Enter` | Copy plain text only |
| `Ctrl+1` .. `Ctrl+9` | Paste the 1st .. 9th visible item |
| `Ctrl+P` | Pin / unpin |
| `Del` | Delete item |
| `Esc` | Clear search, then close |

## Installing / sharing

ClipVault ships as **one self-contained `ClipVault.exe`**. No .NET runtime to install and **no admin rights** at any point: everything it touches lives in your own user profile.

1. Run `ClipVault.exe` from wherever you saved it (Downloads is fine).
2. It offers to **install itself** into `%LocalAppData%\Programs\ClipVault`, start with Windows, and add a Start menu shortcut. Click **Install**. The installed copy starts, and you can delete the downloaded file.
   Prefer a portable app? Click **Run from here** instead; it will not ask again for that file.
3. A tray icon appears and a balloon tells you the hotkey.

To share it with someone else, send them the exe (or the zip from the `dist` folder). It runs on Windows 10 and 11, 64-bit.

**Updating**: run a newer `ClipVault.exe` from anywhere. It notices the older installed copy, offers **Update**, closes the running instance (history is saved first), replaces the file and relaunches.

**Uninstalling**: right-click the tray icon and choose **Uninstall ClipVault...**. It removes the shortcut, the startup entry and the program, and asks whether to delete your history too.

**Start with Windows** can be toggled any time from the tray menu or Settings. If you ever move the exe, the next launch repairs the startup entry automatically.

Windows SmartScreen may show "Windows protected your PC" the first time because the exe is not code-signed. Click **More info**, then **Run anyway**. That is a one-time warning, not an admin prompt.

History and settings live in `%LocalAppData%\ClipVault` (`history.json`, `settings.json`, an `images` folder, and `clipvault.log` if anything goes wrong). Delete that folder to reset the app.

Command line: `ClipVault.exe --portable` skips the install prompt for that launch.

## Building from source

Requirements: Windows, [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```powershell
.\build.ps1
```

That runs `dotnet publish -c Release` and drops `ClipVault.exe` in `dist\`. For a quick debug build use `dotnet build` and run `bin\Debug\net9.0-windows\win-x64\ClipVault.exe`.

The tray/app icon is generated by `Assets\make-icon.ps1` (already checked in as `Assets\ClipVault.ico`; rerun the script only if you change the drawing).

## How it works

- WPF app on .NET 9 with a hidden main window that registers `AddClipboardFormatListener` and a global `RegisterHotKey`.
- `WM_CLIPBOARDUPDATE` events are debounced (150 ms) and then read through the WPF `Clipboard` API with retries, since the copying app often still holds the clipboard lock.
- Each item gets a SHA-256 content hash. New copies matching an existing hash are moved to the top rather than inserted.
- Selecting an item writes it back to the clipboard, hides the window, restores focus to the previous foreground window, and sends `Ctrl+V` via `SendInput`.
- History is a JSON file; images are PNGs stored beside it. Saves are debounced and written atomically.

## About and disclaimer

ClipVault is created by Ovidio Verona and provided free of charge, as is, without warranty of any kind. The full disclaimer is in the app under **About ClipVault** (tray menu or Settings). Remember that it records whatever you copy unless the source app marks it private; exclude your password manager in Settings.

See `CLAUDE.md` for project notes, design decisions, and testing instructions.
