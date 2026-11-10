# Installer / Settings / Uninstall

## Installer (`RpgMakerImageViewer.Setup.exe`)

Wizard steps:

1. Welcome
2. **Choose install path** (where downloaded/installed files are placed). Default: `%LocalAppData%\Programs\RpgMakerImageViewer`
3. Configuration options:
   - Desktop shortcut
   - Start menu shortcut
   - **Associate double-click open** for `.rpgmvp` / `.png_` (default on, no admin)
   - Register Explorer thumbnails (optional, may require elevation)
   - Launch after install
4. Progress / Done

Payload / install layout (easy to find the EXE):

```
{InstallPath}/
  RpgMakerImageViewer.exe                    ← 唯一启动入口（启动器）
  HowToStart.txt
  Uninstall.exe
  bin\                                       ← 程序本体与运行时（用户一般不用进）
  shell\                                     ← 缩略图组件（用户一般不用进）
```

Setup package:

`RpgMakerImageViewer.Setup.exe` is a **single-file** installer with the payload
embedded. Copy that one EXE anywhere and run it — no sibling `payload\` folder
required. (`tools/publish-release.ps1` also writes `dist/Setup/` and a Setup zip.)

Users launch via desktop shortcut or root `RpgMakerImageViewer.exe` — no `.bat`.

## Overwrite / upgrade install

Installing to a directory that already contains this product is supported:

1. Detect existing install (`install.json` / launcher / `bin` / `Uninstall.exe`)
2. Confirm with the user
3. Close running `RpgMakerImageViewer` processes under that directory
4. Clean `bin\` / `shell\` (and stale flat leftovers), then copy new files with overwrite
5. Refresh shortcuts, uninstall registry, and optional associations

Same-directory reinstall therefore upgrades in place.

## File locks on `bin\`

`bin\RpgMakerImageViewer.exe` is the running tray app. While it is open, Windows locks files under `bin\`, so delete/overwrite fails.

Overwrite/uninstall now:

1. Force-close `RpgMakerImageViewer` processes for that install
2. Retry deleting `bin\` / `shell\`
3. If still locked, rename to `bin.old_*` and schedule delayed delete

## Uninstall

- `Uninstall.exe` is a **hard link** to `RpgMakerImageViewer.exe` (one self-contained runtime; copy fallback if hard link fails)
- Double-click `Uninstall.exe` (no args) or run with `/uninstall` — Viewer enters uninstall mode
- Settings window → 卸载程序…
- Windows「应用和功能」entry (`HKCU\...\Uninstall\RpgMakerImageViewer`)

Uninstall removes shortcuts, registry uninstall entry, app/shell files, and attempts to unregister thumbnails.

## In-app settings

Tray → 设置… :

- View install path (read-only)
- Enable/disable **double-click file association**
- Enable/disable Explorer thumbnails
- Minimize to tray on close
- Start with Windows
- Uninstall button
