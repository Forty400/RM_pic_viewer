# Installer / Settings / Uninstall

## Distribution

- **GitHub 主目录**：`RpgMakerImageViewer.Setup.7z`（约 81MB，小于仓库 100MB 限制）  
  解压后得到 `RpgMakerImageViewer.Setup.exe`。
- **本地 / Releases**：`dist/RpgMakerImageViewer.Setup.exe`（单文件，已内嵌 payload）、便携 zip。

`tools/publish-release.ps1` 会同时写出 `dist/` 产物，并在有 7-Zip 时更新仓库根的 `RpgMakerImageViewer.Setup.7z`。

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

Payload / install layout:

```
{InstallPath}/
  RpgMakerImageViewer.exe   ← 启动入口（自包含单文件）
  Uninstall.exe             ← 硬链接到主程序（失败则复制）
  HowToStart.txt
  install.json
  shell\                    ← 缩略图组件（用户一般不用打开）
```

Setup package:

`RpgMakerImageViewer.Setup.exe` is a **single-file** installer with the payload
embedded. Copy that one EXE anywhere and run it — no sibling `payload\` folder
required.

Users launch via desktop shortcut or root `RpgMakerImageViewer.exe` — no `.bat`.

## Overwrite / upgrade install

Installing to a directory that already contains this product is supported:

1. Detect existing install (`install.json` / app exe / `Uninstall.exe`)
2. Confirm with the user
3. Close running `RpgMakerImageViewer` processes under that directory
4. Clean `shell\` / legacy `bin\` / `app\` (and stale flat leftovers), then copy new files with overwrite
5. Refresh shortcuts, uninstall registry, hard-link `Uninstall.exe`, and optional associations

Same-directory reinstall therefore upgrades in place.

## File locks

The tray app locks `RpgMakerImageViewer.exe` while running.

Overwrite/uninstall now:

1. Force-close `RpgMakerImageViewer` processes for that install
2. Retry deleting / replacing files
3. If still locked, rename away and schedule delayed delete

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
