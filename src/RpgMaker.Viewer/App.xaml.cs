using System.IO;
using System.Threading;
using System.Windows;
using RpgMaker.Install;

namespace RpgMaker.Viewer;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;
    private TrayIconService? _tray;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;
    private InstanceIpc? _ipc;
    private bool _exitRequested;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        if (IsUninstallMode(e.Args))
        {
            RunUninstallMode();
            Shutdown();
            return;
        }

        var path = e.Args.FirstOrDefault(a => !a.StartsWith('/') && !a.StartsWith('-'));

        _mutex = new Mutex(true, @"Local\RpgMakerImageViewer.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                if (!InstanceIpc.TrySendOpenPath(Path.GetFullPath(path)))
                {
                    System.Windows.MessageBox.Show(
                        "程序已在运行，但无法将文件交给现有实例。请从托盘退出后重试。",
                        "RPG Maker 图片阅读器",
                        MessageBoxButton.OK,
                        MessageBoxImage.None);
                }
            }
            else
            {
                System.Windows.MessageBox.Show(
                    "程序已在托盘运行，请查看右下角图标。",
                    "RPG Maker 图片阅读器",
                    MessageBoxButton.OK,
                    MessageBoxImage.None);
            }

            Shutdown();
            return;
        }

        _ipc = new InstanceIpc();
        _ipc.OpenPathReceived += p => Dispatch(() => OpenFromExternal(p));
        _ipc.StartServer();

        _mainWindow = new MainWindow(path, trayMode: true);
        _mainWindow.HideToTrayRequested += HideMainWindow;
        _mainWindow.Closed += MainWindow_Closed;

        _tray = new TrayIconService();
        _tray.OpenImageRequested += () => Dispatch(() =>
        {
            EnsureWindow();
            _mainWindow!.PromptOpenFromTray();
        });
        _tray.ShowWindowRequested += () => Dispatch(ShowMainWindow);
        _tray.SettingsRequested += () => Dispatch(ShowSettings);
        _tray.ExitRequested += () => Dispatch(ExitApp);

        // Always show the window so File / View / Help are immediately usable.
        ShowMainWindow();
        if (string.IsNullOrWhiteSpace(path))
            _tray.ShowRunningTip();
    }

    private void OpenFromExternal(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        EnsureWindow();
        _mainWindow!.OpenExternalPath(path);
        ShowMainWindow();
    }

    private void EnsureWindow()
    {
        if (_mainWindow is null)
        {
            _mainWindow = new MainWindow(null, trayMode: true);
            _mainWindow.HideToTrayRequested += HideMainWindow;
            _mainWindow.Closed += MainWindow_Closed;
        }
    }

    private void ShowMainWindow()
    {
        EnsureWindow();
        if (_mainWindow!.IsVisible)
        {
            if (_mainWindow.WindowState == WindowState.Minimized)
                _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
            return;
        }

        _mainWindow.Show();
        _mainWindow.Activate();
        _mainWindow.Focus();
    }

    private void ShowSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow();
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void HideMainWindow()
    {
        _mainWindow?.Hide();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        if (_exitRequested)
            return;
        _mainWindow = null;
    }

    private void ExitApp()
    {
        _exitRequested = true;
        _ipc?.Dispose();
        _ipc = null;
        _tray?.Dispose();
        _tray = null;
        _settingsWindow?.Close();
        _settingsWindow = null;
        _mainWindow?.ForceClose();
        _mainWindow = null;
        try { _mutex?.ReleaseMutex(); } catch { /* ignore */ }
        _mutex?.Dispose();
        _mutex = null;
        Shutdown();
    }

    private void Dispatch(Action action)
    {
        if (Dispatcher.CheckAccess())
            action();
        else
            Dispatcher.Invoke(action);
    }

    private static bool IsUninstallMode(string[] args)
    {
        if (args.Any(a =>
                a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("/u", StringComparison.OrdinalIgnoreCase)))
            return true;

        var exeName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "");
        return exeName.Equals("Uninstall", StringComparison.OrdinalIgnoreCase);
    }

    private static void RunUninstallMode()
    {
        var path = InstallerService.TryGetInstalledPath() ?? "(未检测到安装路径)";
        var confirm = System.Windows.MessageBox.Show(
            $"确定要卸载「RPG Maker 图片阅读器」吗？\n\n安装位置：{path}",
            "卸载",
            MessageBoxButton.YesNo,
            MessageBoxImage.None);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            var log = new System.Text.StringBuilder();
            new InstallerService(msg => log.AppendLine(msg)).Uninstall();
            System.Windows.MessageBox.Show(
                "卸载完成。\n\n" + log.ToString().Trim(),
                "卸载",
                MessageBoxButton.OK,
                MessageBoxImage.None);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                "卸载失败：\n" + ex.Message,
                "卸载",
                MessageBoxButton.OK,
                MessageBoxImage.None);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _ipc?.Dispose();
        _tray?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
