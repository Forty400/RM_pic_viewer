# RPG Maker Image Viewer (Windows)

Decrypt and browse RPG Maker MV/MZ encrypted images (`.rpgmvp`, `.png_`) with Explorer thumbnails and a WPF viewer.

Specs (SPDD): see [`specs/`](specs/).

## Build

```powershell
dotnet build RpgMakerImageViewer.slnx -c Release
dotnet test tests/RpgMaker.Decrypt.Tests/RpgMaker.Decrypt.Tests.csproj -c Release
```

## Viewer / 托盘

```powershell
dotnet run --project src/RpgMaker.Viewer -c Release -- "D:\Games\MyGame\www\img\pictures\Actor1.rpgmvp"
```

- 启动后右下角托盘驻留；右键可「打开图片」或「退出（释放内存）」
- 关闭窗口 / Esc：回到托盘（不退出）
- 左右键：同目录切图

## 用户发布包

```powershell
.\tools\publish-release.ps1
```

生成：
- `dist/Installer/`：**推荐分发**（`RpgMakerImageViewer.Setup.exe` + `payload\`）
- `dist/RpgMakerImageViewer-Setup-win-x64.zip` 安装包
- `dist/RpgMakerImageViewer-win-x64.zip` 便携版

**启动方式（正常软件 .exe，不用 .bat）**
- 安装目录根下只有清晰入口：`RpgMakerImageViewer.exe`（另有 `HowToStart.txt`）；`bin\` / `shell\` 为内部目录
- 也可使用桌面 / 开始菜单快捷方式
- 便携版：解压后同样双击根目录 `RpgMakerImageViewer.exe`

**安装向导**：选择路径 → 配置项 → 安装。  
**卸载**：托盘「设置」→ 卸载，或 `Uninstall.exe`。

## Explorer thumbnails

Admin PowerShell:

```powershell
.\tools\register-shell.ps1 -Configuration Release -ViewerPath "E:\AAAproject\AIprod\RpgMakerImageViewer\src\RpgMaker.Viewer\bin\Release\net8.0-windows\RpgMaker.Viewer.exe"
```

Unregister:

```powershell
.\tools\register-shell.ps1 -Unregister
```

Then restart Explorer or clear the thumbnail cache.

## Deferred

Export decrypted PNG is specified in `specs/export-deferred.md` but not implemented.
