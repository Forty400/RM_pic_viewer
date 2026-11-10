using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace RpgMaker.Viewer;

internal static class ShellRegistration
{
    private const string Clsid = "{A7B3C1D2-4E5F-6789-ABCD-EF0123456789}";
    private const string ThumbHandler = "{e357fccd-a995-4576-b01f-234630154e96}";

    public static void RegisterThumbnails(string? installPath)
    {
        var root = ResolveInstallRoot(installPath);
        var comhost = Path.Combine(root, "shell", "RpgMaker.ThumbnailProvider.comhost.dll");
        if (!File.Exists(comhost))
            throw new FileNotFoundException("未找到缩略图组件，请确认通过安装包完整安装。", comhost);

        RunRegSvr32(comhost, unregister: false);
        foreach (var ext in new[] { ".rpgmvp", ".png_" })
        {
            using var shellEx = Registry.CurrentUser.CreateSubKey(
                $@"Software\Classes\{ext}\ShellEx\{ThumbHandler}");
            shellEx.SetValue(null, Clsid);
        }
    }

    public static void UnregisterThumbnails(string? installPath)
    {
        try
        {
            var root = ResolveInstallRoot(installPath);
            var comhost = Path.Combine(root, "shell", "RpgMaker.ThumbnailProvider.comhost.dll");
            if (File.Exists(comhost))
                RunRegSvr32(comhost, unregister: true);
        }
        catch
        {
            // still clean registry
        }

        foreach (var ext in new[] { ".rpgmvp", ".png_" })
        {
            Registry.CurrentUser.DeleteSubKeyTree(
                $@"Software\Classes\{ext}\ShellEx\{ThumbHandler}", false);
        }
    }

    private static string ResolveInstallRoot(string? installPath)
    {
        if (!string.IsNullOrWhiteSpace(installPath) && Directory.Exists(installPath))
            return installPath;

        var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (Directory.Exists(Path.Combine(appDir, "shell")))
            return appDir;

        // Running from bin\: shell is sibling of bin
        var parent = Directory.GetParent(appDir)?.FullName;
        if (parent is not null && Directory.Exists(Path.Combine(parent, "shell")))
            return parent;

        throw new DirectoryNotFoundException("无法定位安装目录（缺少 shell 组件）。");
    }

    private static void RunRegSvr32(string comhost, bool unregister)
    {
        var args = unregister ? $"/s /u \"{comhost}\"" : $"/s \"{comhost}\"";
        using var p = Process.Start(new ProcessStartInfo
        {
            FileName = "regsvr32.exe",
            Arguments = args,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        }) ?? throw new InvalidOperationException("无法启动 regsvr32");
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"regsvr32 失败（退出码 {p.ExitCode}），请确认已允许管理员权限。");
    }
}
