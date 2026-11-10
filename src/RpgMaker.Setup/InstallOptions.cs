using System.IO;

namespace RpgMaker.Setup;

internal sealed class InstallOptions
{
    public string InstallPath { get; set; } = DefaultInstallPath();
    public bool CreateDesktopShortcut { get; set; } = true;
    public bool CreateStartMenuShortcut { get; set; } = true;
    /// <summary>Associate .rpgmvp/.png_ for double-click open (no admin).</summary>
    public bool AssociateDoubleClickOpen { get; set; } = true;
    public bool RegisterShellThumbnails { get; set; }
    public bool LaunchAfterInstall { get; set; } = true;

    public static string DefaultInstallPath()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "Programs", "RpgMakerImageViewer");
    }
}
