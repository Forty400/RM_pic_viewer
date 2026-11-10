using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace RpgMaker.Viewer;

/// <summary>
/// Registers .rpgmvp / .png_ so double-click opens this viewer like a normal image app.
/// Uses HKCU\Software\Classes (per-user, no admin required).
/// </summary>
internal static class FileAssociation
{
    private const string ProgId = "RpgMakerImageViewer.encryptedimage";
    private static readonly string[] Extensions = [".rpgmvp", ".png_"];

    public static void Register(string? installPath)
    {
        var viewer = ResolveViewerExe(installPath);
        using (var prog = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
        {
            prog.SetValue(null, "RPG Maker 加密图片");
            prog.SetValue("FriendlyTypeName", "RPG Maker 加密图片");
            using var icon = prog.CreateSubKey("DefaultIcon");
            icon.SetValue(null, $"\"{viewer}\",0");
            using var open = prog.CreateSubKey(@"shell\open");
            open.SetValue(null, "打开");
            open.SetValue("FriendlyAppName", "RPG Maker 图片阅读器");
            using var cmd = open.CreateSubKey("command");
            cmd.SetValue(null, $"\"{viewer}\" \"%1\"");
        }

        foreach (var ext in Extensions)
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}");
            key.SetValue(null, ProgId);
            key.SetValue("PerceivedType", "image");
            key.SetValue("Content Type", "image/png");
            using var openWith = key.CreateSubKey("OpenWithProgids");
            openWith.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }

        using (var app = Registry.CurrentUser.CreateSubKey(
                   @"Software\Classes\Applications\RpgMakerImageViewer.exe\shell\open\command"))
        {
            app.SetValue(null, $"\"{viewer}\" \"%1\"");
        }

        NotifyShellChanged();
    }

    public static void Unregister()
    {
        foreach (var ext in Extensions)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ext}", writable: true);
                if (key is null) continue;
                if (string.Equals(key.GetValue(null) as string, ProgId, StringComparison.OrdinalIgnoreCase))
                    key.SetValue(null, "");
                using var openWith = key.OpenSubKey("OpenWithProgids", writable: true);
                openWith?.DeleteValue(ProgId, throwOnMissingValue: false);
            }
            catch
            {
                // ignore
            }
        }

        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(
            @"Software\Classes\Applications\RpgMakerImageViewer.exe",
            throwOnMissingSubKey: false);
        NotifyShellChanged();
    }

    public static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\.rpgmvp");
        return string.Equals(key?.GetValue(null) as string, ProgId, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveViewerExe(string? installPath)
    {
        if (!string.IsNullOrWhiteSpace(installPath))
        {
            // Prefer root launcher (clean entry).
            var root = Path.Combine(installPath, "RpgMakerImageViewer.exe");
            if (File.Exists(root))
                return root;

            var bin = Path.Combine(installPath, "bin", "RpgMakerImageViewer.exe");
            if (File.Exists(bin))
                return bin;

            var legacy = Path.Combine(installPath, "app", "RpgMakerImageViewer.exe");
            if (File.Exists(legacy))
                return legacy;
        }

        // Running from bin\: prefer sibling launcher one level up.
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parentLauncher = Path.Combine(Directory.GetParent(baseDir)?.FullName ?? baseDir, "RpgMakerImageViewer.exe");
        if (File.Exists(parentLauncher) && !string.Equals(parentLauncher, Path.Combine(baseDir, "RpgMakerImageViewer.exe"), StringComparison.OrdinalIgnoreCase))
            return parentLauncher;

        return Environment.ProcessPath
               ?? Path.Combine(baseDir, "RpgMakerImageViewer.exe");
    }

    private static void NotifyShellChanged()
    {
        SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
