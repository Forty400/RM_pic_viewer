using System.IO;

namespace RpgMaker.Install;

public static class InstallPaths
{
    /// <summary>User-facing entry EXE at install root (launcher).</summary>
    public static string GetViewerExe(string installRoot)
    {
        var rootLauncher = Path.Combine(installRoot, InstallConstants.AppExeName);
        if (File.Exists(rootLauncher))
            return rootLauncher;

        var bin = Path.Combine(installRoot, "bin", InstallConstants.AppExeName);
        if (File.Exists(bin))
            return bin;

        var legacyApp = Path.Combine(installRoot, "app", InstallConstants.AppExeName);
        if (File.Exists(legacyApp))
            return legacyApp;

        return rootLauncher;
    }

    public static string GetViewerWorkingDirectory(string installRoot) =>
        Path.GetDirectoryName(GetViewerExe(installRoot)) ?? installRoot;
}
