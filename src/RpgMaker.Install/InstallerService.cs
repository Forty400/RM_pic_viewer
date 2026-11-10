using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace RpgMaker.Install;

public sealed class InstallerService
{
    private readonly Action<string> _log;

    public InstallerService(Action<string> log) => _log = log;

    public string ResolvePayloadRoot() => PayloadLocator.Resolve();

    public static bool IsExistingInstall(string installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
            return false;

        var root = Path.GetFullPath(installPath);
        return File.Exists(Path.Combine(root, "install.json"))
               || File.Exists(Path.Combine(root, InstallConstants.AppExeName))
               || File.Exists(Path.Combine(root, "bin", InstallConstants.AppExeName))
               || File.Exists(Path.Combine(root, "Uninstall.exe"));
    }

    public void Install(InstallOptions options)
    {
        var payload = ResolvePayloadRoot();
        var target = Path.GetFullPath(options.InstallPath);
        var overwrite = IsExistingInstall(target);
        _log(overwrite ? $"覆盖安装到：{target}" : $"安装目录：{target}");
        Directory.CreateDirectory(target);

        if (overwrite)
        {
            CloseRunningAppProcesses(target);
            PrepareTargetForOverwrite(target);
        }

        // Clean user-facing layout:
        //   RpgMakerImageViewer.exe  (launcher only)
        //   Uninstall.exe
        //   HowToStart.txt
        //   bin\   (real app + runtime)
        //   shell\
        InstallFromPayload(payload, target);

        // One runtime copy: Uninstall.exe hard-links (or copies) the Viewer EXE.
        // Viewer enters uninstall mode when started as Uninstall.exe or with /uninstall.
        var uninstallExe = Path.Combine(target, "Uninstall.exe");
        var viewerExe = Path.Combine(target, InstallConstants.AppExeName);
        if (!File.Exists(viewerExe))
            viewerExe = InstallPaths.GetViewerExe(target);
        WriteUninstallEntry(viewerExe, uninstallExe);

        WriteHowToStart(target);
        WriteInstallMeta(target, options);
        WriteAppSettings(target, options);

        viewerExe = InstallPaths.GetViewerExe(target);
        var viewerDir = target; // launcher lives at install root

        if (options.CreateStartMenuShortcut)
        {
            var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            var linkDir = Path.Combine(programs, InstallConstants.ProductName);
            Directory.CreateDirectory(linkDir);
            CreateShortcut(
                Path.Combine(linkDir, $"{InstallConstants.ProductName}.lnk"),
                viewerExe,
                viewerDir);
            CreateShortcut(
                Path.Combine(linkDir, "卸载.lnk"),
                uninstallExe,
                target,
                arguments: "/uninstall");
            _log("已创建开始菜单快捷方式");
        }

        if (options.CreateDesktopShortcut)
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            CreateShortcut(
                Path.Combine(desktop, $"{InstallConstants.ProductName}.lnk"),
                viewerExe,
                viewerDir);
            _log("已创建桌面快捷方式");
        }

        WriteUninstallRegistry(target, uninstallExe);

        if (options.AssociateDoubleClickOpen)
        {
            try
            {
                RegisterFileAssociation(target);
                _log("已关联双击打开（.rpgmvp / .png_）");
            }
            catch (Exception ex)
            {
                _log($"文件关联失败（可稍后在设置中重试）：{ex.Message}");
            }
        }

        if (options.RegisterShellThumbnails)
        {
            try
            {
                RegisterShellThumbnails(target);
                _log("已注册资源管理器缩略图扩展");
            }
            catch (Exception ex)
            {
                _log($"缩略图注册失败（可稍后在设置中重试）：{ex.Message}");
            }
        }

        _log(overwrite ? "覆盖安装完成" : "安装完成");
    }

    private void CloseRunningAppProcesses(string target)
    {
        var root = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;

        // bin\RpgMakerImageViewer.exe (and root launcher) keep files locked while the tray app runs.
        foreach (var process in Process.GetProcessesByName("RpgMakerImageViewer"))
        {
            try
            {
                // MainModule often throws AccessDenied; prefer QueryFullProcessImageName.
                string? path = TryGetProcessPath(process);
                if (string.IsNullOrWhiteSpace(path))
                {
                    try { path = process.MainModule?.FileName; }
                    catch { /* AccessDenied */ }
                }

                if (!string.IsNullOrWhiteSpace(path))
                {
                    var full = Path.GetFullPath(path);
                    if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        continue;
                    _log($"结束占用进程：{full} (PID {process.Id})");
                }
                else
                {
                    // Path unknown: still stop same-named processes so bin\ can be deleted.
                    _log($"结束占用进程（路径未知）PID {process.Id}");
                }

                process.Kill(entireProcessTree: true);
                process.WaitForExit(8000);
            }
            catch (Exception ex)
            {
                _log($"无法结束进程 PID {process.Id}：{ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }

        // Allow antivirus / Explorer handles to release.
        Thread.Sleep(800);
    }

    private static string? TryGetProcessPath(Process process)
    {
        try
        {
            var capacity = 1024;
            var sb = new StringBuilder(capacity);
            var handle = process.Handle;
            if (NativeMethods.QueryFullProcessImageName(handle, 0, sb, ref capacity))
                return sb.ToString(0, capacity);
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private void DeleteDirectoryWithRetry(string dir, string label)
    {
        if (!Directory.Exists(dir))
            return;

        for (var attempt = 1; attempt <= 6; attempt++)
        {
            try
            {
                // Clear read-only flags that block deletes.
                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var attrs = File.GetAttributes(file);
                        if ((attrs & FileAttributes.ReadOnly) != 0)
                            File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
                    }
                    catch { /* ignore */ }
                }

                Directory.Delete(dir, recursive: true);
                _log($"已清理{label}");
                return;
            }
            catch (Exception ex)
            {
                _log($"清理 {label} 第 {attempt} 次失败：{ex.Message}");
                CloseRunningAppProcesses(Path.GetDirectoryName(dir) ?? dir);
                Thread.Sleep(400 * attempt);
            }
        }

        // Last resort: rename away so overwrite can recreate bin\, delayed delete later.
        try
        {
            var trash = dir + ".old_" + DateTime.Now.ToString("yyyyMMddHHmmss");
            Directory.Move(dir, trash);
            _log($"目录占用中，已改名为 {Path.GetFileName(trash)}，稍后可手动删除");
            ScheduleDelete(trash);
        }
        catch (Exception ex)
        {
            _log($"无法移除占用目录 {label}：{ex.Message}");
        }
    }

    private static void ScheduleDelete(string path)
    {
        var bat = Path.Combine(Path.GetTempPath(), "rpgmaker-del-" + Guid.NewGuid().ToString("N") + ".cmd");
        var content = $"""
            @echo off
            timeout /t 3 /nobreak >nul
            rmdir /s /q "{path}"
            del /f /q "{path}" 2>nul
            del "%~f0"
            """;
        File.WriteAllText(bat, content, Encoding.ASCII);
        Process.Start(new ProcessStartInfo
        {
            FileName = bat,
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private void PrepareTargetForOverwrite(string target)
    {
        // Refresh program files; keep Uninstall.exe until replaced, rewrite metadata afterward.
        foreach (var name in new[] { "bin", "shell", "app" })
        {
            var dir = Path.Combine(target, name);
            DeleteDirectoryWithRetry(dir, name + "\\");
        }

        // Remove stale flat runtime leftovers from older layouts (keep launcher/uninstall/docs).
        var keepFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            InstallConstants.AppExeName,
            "Uninstall.exe",
            "HowToStart.txt",
            "install.json",
            "README.md",
            "README-USER.txt"
        };

        foreach (var file in Directory.GetFiles(target))
        {
            var name = Path.GetFileName(file);
            if (keepFiles.Contains(name))
                continue;
            if (!name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                !name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) &&
                !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                continue;

            // Don't delete our own keep list; above already skipped.
            // Extra .exe besides launcher/uninstall from old flat layout:
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals(InstallConstants.AppExeName, StringComparison.OrdinalIgnoreCase) &&
                !name.Equals("Uninstall.exe", StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(file); _log($"已移除旧文件：{name}"); }
                catch (Exception ex) { _log($"跳过旧文件 {name}：{ex.Message}"); }
                continue;
            }

            if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) ||
                (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                 !name.Equals("install.json", StringComparison.OrdinalIgnoreCase)))
            {
                try { File.Delete(file); }
                catch { /* ignore locked leftovers */ }
            }
        }
    }

    public void Uninstall()
    {
        var metaPath = FindInstallMeta();
        if (metaPath is null)
            throw new InvalidOperationException("未找到安装信息，无法卸载。");

        var meta = JsonSerializer.Deserialize<InstallMeta>(File.ReadAllText(metaPath))
            ?? throw new InvalidOperationException("安装信息损坏。");

        var target = meta.InstallPath;
        _log($"卸载目录：{target}");

        // Must stop tray/viewer first — otherwise bin\ stays locked.
        CloseRunningAppProcesses(target);

        try { UnregisterShellThumbnails(target); }
        catch (Exception ex) { _log($"卸载缩略图扩展时忽略错误：{ex.Message}"); }

        try { UnregisterFileAssociation(); }
        catch (Exception ex) { _log($"清理文件关联时忽略错误：{ex.Message}"); }

        RemoveShortcuts();
        RemoveUninstallRegistry();

        // Delete files except running Uninstall.exe — schedule delayed delete if needed
        try
        {
            foreach (var entry in Directory.GetFileSystemEntries(target))
            {
                var name = Path.GetFileName(entry);
                if (name.Equals("Uninstall.exe", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    if (Directory.Exists(entry))
                        DeleteDirectoryWithRetry(entry, name + "\\");
                    else
                        File.Delete(entry);
                }
                catch (Exception ex)
                {
                    _log($"跳过删除 {name}：{ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _log($"删除文件时部分失败：{ex.Message}");
        }

        // Remove remaining install root after Uninstall.exe exits
        ScheduleDelete(target);
        _log("卸载完成，即将清理残留目录（若 bin 仍占用会延后删除）");
    }

    public static string? TryGetInstalledPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{InstallConstants.UninstallKeyName}");
        return key?.GetValue("InstallLocation") as string;
    }

    private string? FindInstallMeta()
    {
        var installed = TryGetInstalledPath();
        if (!string.IsNullOrWhiteSpace(installed))
        {
            var p = Path.Combine(installed, "install.json");
            if (File.Exists(p)) return p;
        }

        // Prefer ProcessPath dir — single-file extract uses a temp AppContext.BaseDirectory.
        foreach (var dir in GetInstallSearchDirs())
        {
            var local = Path.Combine(dir, "install.json");
            if (File.Exists(local))
                return local;
        }

        return null;
    }

    private static List<string> GetInstallSearchDirs()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();

        void Collect(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                var full = Path.GetFullPath(path);
                if (seen.Add(full))
                    list.Add(full);
            }
            catch
            {
                // ignore
            }
        }

        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
            Collect(Path.GetDirectoryName(processPath));

        Collect(AppContext.BaseDirectory);
        if (!string.IsNullOrWhiteSpace(AppContext.BaseDirectory))
            Collect(Path.Combine(AppContext.BaseDirectory, ".."));

        return list;
    }

    private void WriteUninstallEntry(string viewerExe, string uninstallExe)
    {
        if (!File.Exists(viewerExe))
            throw new FileNotFoundException("安装后缺少主程序，无法创建卸载入口。", viewerExe);

        try
        {
            if (File.Exists(uninstallExe))
                File.Delete(uninstallExe);
        }
        catch
        {
            // overwrite via copy below if delete failed
        }

        if (NativeMethods.CreateHardLink(uninstallExe, viewerExe, IntPtr.Zero))
        {
            _log("已写入 Uninstall.exe（硬链接到主程序，不额外占用磁盘）");
            return;
        }

        File.Copy(viewerExe, uninstallExe, overwrite: true);
        _log("已写入 Uninstall.exe（硬链接失败，已复制主程序；磁盘占用约两倍）");
    }

    private void WriteInstallMeta(string target, InstallOptions options)
    {
        var meta = new InstallMeta
        {
            InstallPath = target,
            Version = InstallConstants.Version,
            RegisterShellThumbnails = options.RegisterShellThumbnails,
            InstalledAtUtc = DateTime.UtcNow
        };
        var json = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(target, "install.json"), json);
    }

    private static void WriteAppSettings(string target, InstallOptions options)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RpgMakerImageViewer");
        Directory.CreateDirectory(dir);
        var settings = new Dictionary<string, object?>
        {
            ["installPath"] = target,
            ["enableDoubleClickOpen"] = options.AssociateDoubleClickOpen,
            ["enableExplorerThumbnails"] = options.RegisterShellThumbnails,
            ["startWithWindows"] = false,
            ["minimizeToTrayOnClose"] = true
        };
        File.WriteAllText(
            Path.Combine(dir, "settings.json"),
            JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void WriteUninstallRegistry(string target, string uninstallExe)
    {
        using var key = Registry.CurrentUser.CreateSubKey(
            $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{InstallConstants.UninstallKeyName}");
        key.SetValue("DisplayName", InstallConstants.ProductName);
        key.SetValue("DisplayVersion", InstallConstants.Version);
        key.SetValue("Publisher", InstallConstants.Publisher);
        key.SetValue("InstallLocation", target);
        key.SetValue("UninstallString", $"\"{uninstallExe}\" /uninstall");
        key.SetValue("DisplayIcon", InstallPaths.GetViewerExe(target));
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", EstimateSizeKb(target), RegistryValueKind.DWord);
        _log("已写入“应用和功能”卸载信息");
    }

    private static void RemoveUninstallRegistry()
    {
        Registry.CurrentUser.DeleteSubKeyTree(
            $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{InstallConstants.UninstallKeyName}",
            throwOnMissingSubKey: false);
    }

    private static int EstimateSizeKb(string target)
    {
        try
        {
            long bytes = 0;
            foreach (var file in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
                bytes += new FileInfo(file).Length;
            return (int)Math.Max(1, bytes / 1024);
        }
        catch
        {
            return 1;
        }
    }

    private const string AssocProgId = "RpgMakerImageViewer.encryptedimage";

    private void InstallFromPayload(string payload, string target)
    {
        var payloadBin = Path.Combine(payload, "bin");
        var payloadApp = Path.Combine(payload, "app");
        var payloadShell = Path.Combine(payload, "shell");
        var appSrc = Path.Combine(payload, InstallConstants.AppExeName);

        // Slim layout: single-file Viewer at payload root (no separate launcher/runtime bin).
        if (File.Exists(appSrc) && !Directory.Exists(payloadBin))
        {
            File.Copy(appSrc, Path.Combine(target, InstallConstants.AppExeName), overwrite: true);
            _log("已安装 RpgMakerImageViewer.exe");
        }
        else if (File.Exists(appSrc) && Directory.Exists(payloadBin))
        {
            File.Copy(appSrc, Path.Combine(target, InstallConstants.AppExeName), overwrite: true);
            _log("已安装启动入口 RpgMakerImageViewer.exe");
            CopyDirectory(payloadBin, Path.Combine(target, "bin"));
        }
        else if (Directory.Exists(payloadBin) && File.Exists(Path.Combine(payloadBin, InstallConstants.AppExeName)))
        {
            CopyDirectory(payloadBin, Path.Combine(target, "bin"));
            _log("警告：缺少根目录启动器，请从 bin\\RpgMakerImageViewer.exe 启动");
        }
        else if (Directory.Exists(payloadApp) && File.Exists(Path.Combine(payloadApp, InstallConstants.AppExeName)))
        {
            CopyDirectory(payloadApp, Path.Combine(target, "bin"));
            _log("已按兼容模式安装到 bin\\");
        }
        else
        {
            throw new DirectoryNotFoundException("安装包内容不完整：缺少 RpgMakerImageViewer.exe。");
        }

        if (Directory.Exists(payloadShell))
            CopyDirectory(payloadShell, Path.Combine(target, "shell"));
    }

    private static void WriteHowToStart(string target)
    {
        var text =
            "【如何启动】\r\n" +
            "1. 双击本目录下的 RpgMakerImageViewer.exe\r\n" +
            "2. 或使用桌面 / 开始菜单的「RPG Maker 图片阅读器」快捷方式\r\n\r\n" +
            "说明：shell 是缩略图组件目录，一般不用打开。\r\n" +
            "卸载请运行 Uninstall.exe。\r\n";
        File.WriteAllText(Path.Combine(target, "HowToStart.txt"), text);
    }

    private void RegisterFileAssociation(string target)
    {
        var viewer = InstallPaths.GetViewerExe(target);
        using (var prog = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{AssocProgId}"))
        {
            prog.SetValue(null, "RPG Maker 加密图片");
            prog.SetValue("FriendlyTypeName", "RPG Maker 加密图片");
            using var icon = prog.CreateSubKey("DefaultIcon");
            icon.SetValue(null, $"\"{viewer}\",0");
            using var open = prog.CreateSubKey(@"shell\open");
            open.SetValue(null, "打开");
            open.SetValue("FriendlyAppName", InstallConstants.ProductName);
            using var cmd = open.CreateSubKey("command");
            cmd.SetValue(null, $"\"{viewer}\" \"%1\"");
        }

        foreach (var ext in new[] { ".rpgmvp", ".png_" })
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}");
            key.SetValue(null, AssocProgId);
            key.SetValue("PerceivedType", "image");
            key.SetValue("Content Type", "image/png");
            using var openWith = key.CreateSubKey("OpenWithProgids");
            openWith.SetValue(AssocProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }

        using (var app = Registry.CurrentUser.CreateSubKey(
                   @"Software\Classes\Applications\RpgMakerImageViewer.exe\shell\open\command"))
        {
            app.SetValue(null, $"\"{viewer}\" \"%1\"");
        }

        NativeMethods.SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
    }

    private static void UnregisterFileAssociation()
    {
        foreach (var ext in new[] { ".rpgmvp", ".png_" })
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ext}", writable: true);
            if (key is not null &&
                string.Equals(key.GetValue(null) as string, AssocProgId, StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue(null, "");
            }

            using var openWith = key?.OpenSubKey("OpenWithProgids", writable: true);
            openWith?.DeleteValue(AssocProgId, throwOnMissingValue: false);
        }

        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{AssocProgId}", false);
        Registry.CurrentUser.DeleteSubKeyTree(
            @"Software\Classes\Applications\RpgMakerImageViewer.exe", false);
        NativeMethods.SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
    }

    private void RegisterShellThumbnails(string target)
    {
        var comhost = Path.Combine(target, "shell", "RpgMaker.ThumbnailProvider.comhost.dll");
        if (!File.Exists(comhost))
            throw new FileNotFoundException("缺少缩略图组件", comhost);

        RunRegSvr32(comhost, unregister: false);
        const string clsid = "{A7B3C1D2-4E5F-6789-ABCD-EF0123456789}";
        const string thumbHandler = "{e357fccd-a995-4576-b01f-234630154e96}";
        foreach (var ext in new[] { ".rpgmvp", ".png_" })
        {
            using var shellEx = Registry.CurrentUser.CreateSubKey(
                $@"Software\Classes\{ext}\ShellEx\{thumbHandler}");
            shellEx.SetValue(null, clsid);
        }
    }

    private void UnregisterShellThumbnails(string target)
    {
        var comhost = Path.Combine(target, "shell", "RpgMaker.ThumbnailProvider.comhost.dll");
        if (File.Exists(comhost))
        {
            try { RunRegSvr32(comhost, unregister: true); }
            catch { /* ignore */ }
        }

        const string thumbHandler = "{e357fccd-a995-4576-b01f-234630154e96}";
        foreach (var ext in new[] { ".rpgmvp", ".png_" })
        {
            Registry.CurrentUser.DeleteSubKeyTree(
                $@"Software\Classes\{ext}\ShellEx\{thumbHandler}", false);
        }
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        public static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        public static extern bool QueryFullProcessImageName(
            IntPtr hProcess,
            int dwFlags,
            StringBuilder lpExeName,
            ref int lpdwSize);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        public static extern bool CreateHardLink(
            string lpFileName,
            string lpExistingFileName,
            IntPtr lpSecurityAttributes);
    }

    private static void RunRegSvr32(string comhost, bool unregister)
    {
        var args = unregister ? $"/s /u \"{comhost}\"" : $"/s \"{comhost}\"";
        var psi = new ProcessStartInfo
        {
            FileName = "regsvr32.exe",
            Arguments = args,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 regsvr32");
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"regsvr32 退出码 {p.ExitCode}（注册缩略图通常需要管理员权限）");
    }

    private void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, dir);
            Directory.CreateDirectory(Path.Combine(destination, rel));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            var dest = Path.Combine(destination, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
            _log($"复制 {rel}");
        }
    }

    private static void RemoveShortcuts()
    {
        var desktop = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            $"{InstallConstants.ProductName}.lnk");
        if (File.Exists(desktop)) File.Delete(desktop);

        var programs = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            InstallConstants.ProductName);
        if (Directory.Exists(programs))
            Directory.Delete(programs, recursive: true);
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDir, string? arguments = null)
    {
        // Use Windows Script Host via temporary VBS to avoid extra COM package deps
        var vbs = Path.Combine(Path.GetTempPath(), "mkshortcut-" + Guid.NewGuid().ToString("N") + ".vbs");
        var argsLine = string.IsNullOrWhiteSpace(arguments) ? "" : $"oLink.Arguments = \"{EscapeVbs(arguments)}\"";
        var script = $"""
            Set oWS = WScript.CreateObject("WScript.Shell")
            Set oLink = oWS.CreateShortcut("{EscapeVbs(shortcutPath)}")
            oLink.TargetPath = "{EscapeVbs(targetPath)}"
            oLink.WorkingDirectory = "{EscapeVbs(workingDir)}"
            {argsLine}
            oLink.Save
            """;
        File.WriteAllText(vbs, script, Encoding.Unicode);
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "wscript.exe",
                Arguments = $"\"{vbs}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            })!;
            p.WaitForExit(10000);
        }
        finally
        {
            if (File.Exists(vbs)) File.Delete(vbs);
        }
    }

    private static string EscapeVbs(string value) => value.Replace("\"", "\"\"");

    private sealed class InstallMeta
    {
        public string InstallPath { get; set; } = "";
        public string Version { get; set; } = "";
        public bool RegisterShellThumbnails { get; set; }
        public DateTime InstalledAtUtc { get; set; }
    }
}
