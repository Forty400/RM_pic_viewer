using System.Diagnostics;
using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;

namespace RpgMaker.Setup;

public partial class MainWindow : Window
{
    private enum Step
    {
        Welcome,
        Path,
        Options,
        Progress,
        Done,
        UninstallConfirm
    }

    private readonly bool _uninstallMode;
    private readonly InstallOptions _options = new();
    private Step _step;
    private bool _wasOverwrite;

    public MainWindow(bool uninstallMode)
    {
        _uninstallMode = uninstallMode;
        InitializeComponent();
        Title = uninstallMode
            ? "RPG Maker 图片阅读器 - 卸载"
            : "RPG Maker 图片阅读器 - 安装";

        InstallPathBox.Text = _options.InstallPath;
        _step = uninstallMode ? Step.UninstallConfirm : Step.Welcome;
        if (uninstallMode)
        {
            var path = InstallerService.TryGetInstalledPath() ?? "（未检测到安装路径）";
            UninstallPathText.Text = $"当前安装位置：{path}";
        }

        ShowStep();
    }

    private void ShowStep()
    {
        PageWelcome.Visibility = Visibility.Collapsed;
        PagePath.Visibility = Visibility.Collapsed;
        PageOptions.Visibility = Visibility.Collapsed;
        PageProgress.Visibility = Visibility.Collapsed;
        PageDone.Visibility = Visibility.Collapsed;
        PageUninstall.Visibility = Visibility.Collapsed;

        BackButton.IsEnabled = _step is Step.Path or Step.Options;
        NextButton.IsEnabled = true;
        CancelButton.IsEnabled = _step is not Step.Progress;

        switch (_step)
        {
            case Step.Welcome:
                PageWelcome.Visibility = Visibility.Visible;
                NextButton.Content = "下一步";
                break;
            case Step.Path:
                PagePath.Visibility = Visibility.Visible;
                NextButton.Content = "下一步";
                break;
            case Step.Options:
                PageOptions.Visibility = Visibility.Visible;
                NextButton.Content = "安装";
                break;
            case Step.Progress:
                PageProgress.Visibility = Visibility.Visible;
                NextButton.IsEnabled = false;
                BackButton.IsEnabled = false;
                break;
            case Step.Done:
                PageDone.Visibility = Visibility.Visible;
                NextButton.Content = "完成";
                BackButton.IsEnabled = false;
                CancelButton.IsEnabled = false;
                break;
            case Step.UninstallConfirm:
                PageUninstall.Visibility = Visibility.Visible;
                NextButton.Content = "卸载";
                BackButton.IsEnabled = false;
                break;
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        _step = _step switch
        {
            Step.Path => Step.Welcome,
            Step.Options => Step.Path,
            _ => _step
        };
        ShowStep();
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        switch (_step)
        {
            case Step.Welcome:
                _step = Step.Path;
                ShowStep();
                break;
            case Step.Path:
                if (!ValidatePath()) return;
                if (!ConfirmOverwriteIfNeeded()) return;
                _step = Step.Options;
                ShowStep();
                break;
            case Step.Options:
                ReadOptions();
                _wasOverwrite = InstallerService.IsExistingInstall(_options.InstallPath);
                await RunInstallAsync();
                break;
            case Step.UninstallConfirm:
                await RunUninstallAsync();
                break;
            case Step.Done:
                if (!_uninstallMode && OptLaunch.IsChecked == true)
                    TryLaunchApp();
                Close();
                break;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "选择程序文件存放目录",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(InstallPathBox.Text)
                ? InstallPathBox.Text
                : InstallOptions.DefaultInstallPath()
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
            InstallPathBox.Text = dialog.SelectedPath;
    }

    private bool ValidatePath()
    {
        var path = InstallPathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            System.Windows.MessageBox.Show(this, "请填写安装路径。", "提示",
                MessageBoxButton.OK, MessageBoxImage.None);
            return false;
        }

        try
        {
            path = Path.GetFullPath(path);
            Directory.CreateDirectory(path);
            InstallPathBox.Text = path;
            _options.InstallPath = path;
            OverwriteHint.Visibility = InstallerService.IsExistingInstall(path)
                ? Visibility.Visible
                : Visibility.Collapsed;
            return true;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"无法使用该路径：{ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.None);
            return false;
        }
    }

    private bool ConfirmOverwriteIfNeeded()
    {
        var path = _options.InstallPath;
        if (!InstallerService.IsExistingInstall(path))
            return true;

        var result = System.Windows.MessageBox.Show(
            this,
            $"目标目录已安装本软件：\n{path}\n\n是否覆盖安装（更新到新版本）？\n安装前会尝试关闭正在运行的程序。",
            "覆盖安装",
            MessageBoxButton.YesNo,
            MessageBoxImage.None);
        return result == MessageBoxResult.Yes;
    }

    private void ReadOptions()
    {
        _options.InstallPath = InstallPathBox.Text.Trim();
        _options.CreateDesktopShortcut = OptDesktop.IsChecked == true;
        _options.CreateStartMenuShortcut = OptStartMenu.IsChecked == true;
        _options.AssociateDoubleClickOpen = OptAssoc.IsChecked == true;
        _options.RegisterShellThumbnails = OptShell.IsChecked == true;
        _options.LaunchAfterInstall = OptLaunch.IsChecked == true;
    }

    private async Task RunInstallAsync()
    {
        _step = Step.Progress;
        ShowStep();
        LogText.Text = "";
        var service = new InstallerService(AppendLog);

        try
        {
            await Task.Run(() => service.Install(_options));
            DoneTitle.Text = _wasOverwrite ? "覆盖安装完成" : "安装完成";
            DoneBody.Text =
                $"程序已安装到：\n{_options.InstallPath}\n\n" +
                "可从开始菜单、桌面快捷方式，或目录中的 RpgMakerImageViewer.exe 启动。";
            _step = Step.Done;
            ShowStep();
        }
        catch (Exception ex)
        {
            AppendLog("失败：" + ex.Message);
            System.Windows.MessageBox.Show(this, ex.Message, "安装失败",
                MessageBoxButton.OK, MessageBoxImage.None);
            _step = Step.Options;
            ShowStep();
        }
    }

    private async Task RunUninstallAsync()
    {
        _step = Step.Progress;
        ShowStep();
        LogText.Text = "";
        var service = new InstallerService(AppendLog);

        try
        {
            await Task.Run(service.Uninstall);
            DoneTitle.Text = "卸载完成";
            DoneBody.Text = "程序已卸载。若仍有残留文件夹，将在数秒后自动清理。";
            _step = Step.Done;
            ShowStep();
        }
        catch (Exception ex)
        {
            AppendLog("失败：" + ex.Message);
            System.Windows.MessageBox.Show(this, ex.Message, "卸载失败",
                MessageBoxButton.OK, MessageBoxImage.None);
            _step = Step.UninstallConfirm;
            ShowStep();
        }
    }

    private void AppendLog(string line)
    {
        Dispatcher.Invoke(() =>
        {
            LogText.Text += line + Environment.NewLine;
        });
    }

    private void TryLaunchApp()
    {
        var exe = InstallPaths.GetViewerExe(_options.InstallPath);
        if (!File.Exists(exe)) return;
        Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = InstallPaths.GetViewerWorkingDirectory(_options.InstallPath),
            UseShellExecute = true
        });
    }
}
