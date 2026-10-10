# Fullscreen Start

**English** · [Deutsch](README.de.md)

The Windows 10 fullscreen Start screen, rebuilt for Windows 11. C# / WPF on .NET 8; runs in the
notification area and opens instantly.

## Features

- Tile groups in columns, freely placeable, with folders and four tile sizes
- "All apps" list and type-to-search (Enter launches the first hit)
- Blurred wallpaper background (also works with Lively Wallpaper), follows light/dark mode and accent colour
- Opens with the **Windows key**, the **taskbar Start button** or **Ctrl+Shift+Space**; Win+… shortcuts keep working

## Install

```bash
powershell -ExecutionPolicy Bypass -File tools/install.ps1
```

Installs to `%LocalAppData%\Programs\FullscreenStart` and starts with Windows (`-NoAutostart` to skip).
`tools/uninstall.ps1` removes it again (`-RemoveData` also deletes layout and settings).

## Usage

| Action | How |
| --- | --- |
| Close | Esc, click on empty space, or launch an app |
| Move a tile | Drag it: onto another tile's centre = folder, onto its edge = insert before/after |
| New group | Drag a tile into empty space; the group appears right there |
| Move a group | Drag its header: onto a group = before/after it, into empty space = place freely (hold at the bottom edge to scroll further) |
| Rename a group | Click its header |
| More | Right-click: pin/unpin, resize, dissolve folder, run as administrator, open file location, close the gap above a group |

The tray menu toggles the Windows key, the Start button and autostart.

## Settings and data

Everything lives in `%AppData%\FullscreenStart`:

- `layout.json`: columns, groups, tiles and folders
- `settings.json`: hotkey, `theme` (`system`/`light`/`dark`), background blur and tint, tile labels, start view
- `log.txt`: start and error log

Delete `layout.json` to get the default layout back.

## Limitations

- If an elevated window (e.g. Task Manager) is in the foreground, Windows does not pass the Windows key
  to the app, and its own Start menu opens instead.
- Autostart deliberately runs without admin rights; otherwise every app launched from the menu would run elevated.

## Development

```bash
dotnet build
dotnet test
```

`tools/snapshot.ps1` renders the views to PNG without showing the window.
`FullscreenStart.exe --selftest <file>` drives clicks and drag & drop through the real layout.
