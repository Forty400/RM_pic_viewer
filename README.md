# RM_pic_viewer

RPG Maker 图片预览程序（Windows）

Decrypt and browse RPG Maker MV/MZ encrypted images (`.rpgmvp`, `.png_`) with Explorer thumbnails and a WPF viewer.

Specs (SPDD): see [`specs/`](specs/).

## 下载

1. **推荐（仓库主目录）**：[RpgMakerImageViewer.Setup.7z](https://github.com/Forty400/RM_pic_viewer/raw/main/RpgMakerImageViewer.Setup.7z)（约 81MB）  
   用 [7-Zip](https://www.7-zip.org/) 解压 → 双击 `RpgMakerImageViewer.Setup.exe` 安装。
2. [GitHub Releases](https://github.com/Forty400/RM_pic_viewer/releases/latest)：安装包 / 便携 zip（体积更大，可走附件）。

说明：未压缩的单文件 Setup 约 130MB+，超过 GitHub **仓库** 100MB 限制，因此主目录只放 **7z**。

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
- 左右键：同目录切图（切换时保留当前缩放）

## 用户发布包

```powershell
.\tools\publish-release.ps1
```

生成：
- `RpgMakerImageViewer.Setup.7z`（仓库根，推荐上传 GitHub 主目录，小于 100MB）
- `dist/RpgMakerImageViewer.Setup.exe` / `dist/Setup/`（完整单文件安装器）
- `dist/RpgMakerImageViewer-Setup-win-x64.zip` 安装包 zip
- `dist/RpgMakerImageViewer-win-x64.zip` 便携版

**启动方式（正常软件 .exe，不用 .bat）**
- 安装目录根：`RpgMakerImageViewer.exe`（另有 `HowToStart.txt`）；`shell\` 为缩略图组件
- 也可使用桌面 / 开始菜单快捷方式
- 便携版：解压后同样双击根目录 `RpgMakerImageViewer.exe`

**安装向导**：选择路径 → 配置项 → 安装。  
**卸载**：托盘「设置」→ 卸载，或双击 `Uninstall.exe`（与主程序硬链接，不额外占磁盘）。

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
