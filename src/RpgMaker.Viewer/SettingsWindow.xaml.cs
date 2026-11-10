using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace RpgMaker.Viewer;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public SettingsWindow()
    {
        InitializeComponent();
        _settings = AppSettings.Load();
        InstallPathBox.Text = string.IsNullOrWhiteSpace(_settings.InstallPath)
            ? "（未通过安装包安装，或尚未记录路径）"
            : _settings.InstallPath;
        OptDoubleClick.IsChecked = _settings.EnableDoubleClickOpen || FileAssociation.IsRegistered();
        OptMinimizeTray.IsChecked = _settings.MinimizeToTrayOnClose;
        OptStartWithWindows.IsChecked = _settings.StartWithWindows;
        OptShell.IsChecked = _settings.EnableExplorerThumbnails;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.MinimizeToTrayOnClose = OptMinimizeTray.IsChecked == true;
        _settings.StartWithWindows = OptStartWithWindows.IsChecked == true;
        var wantAssoc = OptDoubleClick.IsChecked == true;
        var wantShell = OptShell.IsChecked == true;

        try
        {
            ApplyStartWithWindows(_settings.StartWithWindows);

            if (wantAssoc)
                FileAssociation.Register(_settings.InstallPath);
            else
                FileAssociation.Unregister();

            if (wantShell != _settings.EnableExplorerThumbnails)
            {
                if (wantShell)
                    ShellRegistration.RegisterThumbnails(_settings.InstallPath);
                else
                    ShellRegistration.UnregisterThumbnails(_settings.InstallPath);
            }

            _settings.EnableDoubleClickOpen = wantAssoc;
            _settings.EnableExplorerThumbnails = wantShell;
            _settings.Save();
            System.Windows.MessageBox.Show(this,
                "设置已保存。\n若双击仍未用本程序打开，可在资源管理器中对该文件选「打开方式 → 始终使用此应用」。",
                "设置",
                MessageBoxButton.OK,
                MessageBoxImage.None);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "保存失败",
                MessageBoxButton.OK, MessageBoxImage.None);
        }
    }

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        var confirm = System.Windows.MessageBox.Show(
            this,
            "确定要卸载本程序吗？将启动卸载向导。",
            "卸载",
            MessageBoxButton.YesNo,
            MessageBoxImage.None);
        if (confirm != MessageBoxResult.Yes)
            return;

        var uninstall = FindUninstallExe(_settings.InstallPath);
        if (uninstall is null)
        {
            System.Windows.MessageBox.Show(this,
                "未找到 Uninstall.exe。\n\n请到安装目录双击 Uninstall.exe，\n或在系统「应用和功能」中卸载，\n或重新运行安装包。",
                "卸载",
                MessageBoxButton.OK,
                MessageBoxImage.None);
            return;
        }

        try
        {
            var started = Process.Start(new ProcessStartInfo
            {
                FileName = uninstall,
                Arguments = "/uninstall",
                WorkingDirectory = Path.GetDirectoryName(uninstall)!,
                UseShellExecute = true
            });
            if (started is null)
            {
                System.Windows.MessageBox.Show(this,
                    $"无法启动卸载程序：\n{uninstall}",
                    "卸载",
                    MessageBoxButton.OK,
                    MessageBoxImage.None);
                return;
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this,
                $"启动卸载程序失败：\n{ex.Message}\n\n路径：{uninstall}",
                "卸载",
                MessageBoxButton.OK,
                MessageBoxImage.None);
            return;
        }

        System.Windows.Application.Current.Shutdown();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static string? FindUninstallExe(string? installPath)
    {
        foreach (var candidate in EnumerateUninstallCandidates(installPath))
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateUninstallCandidates(string? installPath)
    {
        if (!string.IsNullOrWhiteSpace(installPath))
            yield return Path.Combine(installPath, "Uninstall.exe");

        // Running from bin\: Uninstall.exe is one level up.
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Directory.GetParent(baseDir)?.FullName;
        if (!string.IsNullOrWhiteSpace(parent))
            yield return Path.Combine(parent, "Uninstall.exe");

        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\RpgMakerImageViewer");
        var uninstallString = key?.GetValue("UninstallString") as string;
        if (string.IsNullOrWhiteSpace(uninstallString))
            yield break;

        var trimmed = uninstallString.Trim();
        if (trimmed.StartsWith('"'))
        {
            var end = trimmed.IndexOf('"', 1);
            if (end > 1)
            {
                yield return trimmed[1..end];
                yield break;
            }
        }

        yield return trimmed.Split(' ', 2)[0];
    }

    private static void ApplyStartWithWindows(bool enabled)
    {
        const string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        using var key = Registry.CurrentUser.CreateSubKey(runKey);
        const string valueName = "RpgMakerImageViewer";
        if (enabled)
        {
            var exe = Environment.ProcessPath
                ?? Path.Combine(AppContext.BaseDirectory, "RpgMakerImageViewer.exe");
            key.SetValue(valueName, $"\"{exe}\"");
        }
        else
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
        }
    }
}
