using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;
using RpgMaker.Decrypt;

namespace RpgMaker.Viewer;

public partial class MainWindow : Window
{
    private const double MinZoom = 0.05;
    private const double MaxZoom = 16.0;
    private const double ZoomStep = 1.15;

    private readonly Decryptor _decryptor = new();
    private readonly bool _trayMode;
    private IReadOnlyList<string> _siblings = Array.Empty<string>();
    private int _index = -1;
    private bool _forceClose;
    private bool _fitToWindow = true;
    private double _zoom = 1.0;
    private System.Windows.Size _naturalSize;

    public event Action? HideToTrayRequested;

    public MainWindow(string? initialPath, bool trayMode = false)
    {
        _trayMode = trayMode;
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            if (_fitToWindow && PreviewImage.Source is not null)
                ApplyFitToWindow();
        };

        if (_trayMode)
            Closing += MainWindow_Closing;

        Loaded += (_, _) =>
        {
            Activate();
            Focus();
            if (!string.IsNullOrWhiteSpace(initialPath) && File.Exists(initialPath))
                OpenPath(initialPath);
            else if (!_trayMode)
                PromptOpenImage();
        };
    }

    public void PromptOpenFromTray()
    {
        EnsureVisible();
        PromptOpenImage();
    }

    public void OpenExternalPath(string path)
    {
        EnsureVisible();
        OpenPath(path);
        Focus();
    }

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    private void EnsureVisible()
    {
        if (!IsVisible)
        {
            Show();
            Activate();
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_forceClose || !_trayMode)
            return;

        var minimizeToTray = AppSettings.Load().MinimizeToTrayOnClose;
        if (!minimizeToTray)
            return;

        e.Cancel = true;
        Hide();
        HideToTrayRequested?.Invoke();
    }

    private void FileOpenImage_Click(object sender, RoutedEventArgs e) => PromptOpenImage();

    private void FileOpenFolder_Click(object sender, RoutedEventArgs e) => PromptOpenFolder();

    private void FileClose_Click(object sender, RoutedEventArgs e)
    {
        if (_trayMode)
        {
            Hide();
            HideToTrayRequested?.Invoke();
        }
        else
        {
            Close();
        }
    }

    private void ViewFit_Click(object sender, RoutedEventArgs e) => SetFitToWindow();

    private void ViewZoom_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag })
            return;
        if (!double.TryParse(tag, NumberStyles.Float, CultureInfo.InvariantCulture, out var zoom))
            return;
        SetZoom(zoom);
    }

    private void ViewZoomIn_Click(object sender, RoutedEventArgs e) => ZoomBy(ZoomStep);

    private void ViewZoomOut_Click(object sender, RoutedEventArgs e) => ZoomBy(1.0 / ZoomStep);

    private void HelpAboutApp_Click(object sender, RoutedEventArgs e)
    {
        // MessageBoxImage.None：避免系统提示音
        System.Windows.MessageBox.Show(
            this,
            $"{AppInfo.ProductName}\n版本 {AppInfo.Version}\n\n{AppInfo.Description}\n\n" +
            "功能：加密图片预览、左右切换、缩放、文件夹浏览、托盘驻留。",
            "关于软件",
            MessageBoxButton.OK,
            MessageBoxImage.None);
    }

    private void HelpAboutAuthor_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            this,
            $"作者：{AppInfo.AuthorName}\nGitHub：{AppInfo.AuthorGitHubUrl}\n\n是否打开 GitHub 主页？",
            "关于作者",
            MessageBoxButton.YesNo,
            MessageBoxImage.None);

        if (result == MessageBoxResult.Yes)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = AppInfo.AuthorGitHubUrl,
                UseShellExecute = true
            });
        }
    }

    private void PromptOpenImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "RPG Maker / 图片|*.rpgmvp;*.png_;*.png|所有文件|*.*",
            Title = "打开图片"
        };

        if (dialog.ShowDialog(this) == true)
            OpenPath(dialog.FileName);
        else if (PreviewImage.Source is null)
            ShowStatus("未打开图片。可用 File → 打开图片 / 打开文件夹。");
    }

    private void PromptOpenFolder()
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "选择包含图片的文件夹",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() != Forms.DialogResult.OK)
            return;

        OpenFolder(dialog.SelectedPath);
    }

    private void OpenFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            ShowStatus($"文件夹不存在：\n{folder}");
            return;
        }

        var list = Directory.EnumerateFiles(folder)
            .Where(p => ImageFormats.IsViewerImageExtension(Path.GetExtension(p)))
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (list.Count == 0)
        {
            ShowStatus($"该文件夹下没有可预览的图片（.rpgmvp / .png_ / .png）：\n{folder}");
            return;
        }

        _siblings = list;
        _index = 0;
        ShowCurrent();
    }

    private void OpenPath(string path)
    {
        if (Directory.Exists(path))
        {
            OpenFolder(path);
            return;
        }

        _siblings = ImageFormats.ListSiblingImages(path);
        _index = _siblings.ToList().FindIndex(p =>
            string.Equals(p, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));

        if (_index < 0)
        {
            _siblings = new[] { Path.GetFullPath(path) };
            _index = 0;
        }

        ShowCurrent();
    }

    private void ShowCurrent()
    {
        if (_index < 0 || _index >= _siblings.Count)
        {
            ShowStatus("没有可显示的图片。");
            return;
        }

        var path = _siblings[_index];
        try
        {
            var bytes = _decryptor.LoadImageBytes(path);
            using var ms = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();

            PreviewImage.Source = bitmap;
            PreviewImage.Visibility = Visibility.Visible;
            StatusText.Visibility = Visibility.Collapsed;
            _naturalSize = new System.Windows.Size(bitmap.PixelWidth, bitmap.PixelHeight);
            Title = $"{AppInfo.ProductName} — {Path.GetFileName(path)} ({_index + 1}/{_siblings.Count})";
            PathStatusText.Text = path;
            // 左右切换时保留当前 View 缩放（适应窗口则按新图重算；固定比例则保持数值）
            ApplyRememberedView();
        }
        catch (Exception ex)
        {
            PreviewImage.Source = null;
            ShowStatus($"打开失败：\n{path}\n\n{ex.Message}");
            Title = $"{AppInfo.ProductName} — {Path.GetFileName(path)}";
            PathStatusText.Text = path;
        }
    }

    private void ApplyRememberedView()
    {
        if (_fitToWindow)
            SetFitToWindow();
        else
            ApplyScale(_zoom, keepFitFlag: false);
    }

    private void ShowStatus(string message)
    {
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
        PreviewImage.Visibility = Visibility.Collapsed;
        PathStatusText.Text = "";
    }

    private void SetFitToWindow()
    {
        _fitToWindow = true;
        ApplyFitToWindow();
        UpdateZoomStatus();
    }

    private void ApplyFitToWindow()
    {
        if (PreviewImage.Source is null || _naturalSize.Width <= 0 || _naturalSize.Height <= 0)
            return;

        var viewportW = Math.Max(1, ImageScroll.ViewportWidth);
        var viewportH = Math.Max(1, ImageScroll.ViewportHeight);
        if (viewportW <= 1 || viewportH <= 1)
        {
            // Layout may not be ready yet.
            Dispatcher.BeginInvoke(ApplyFitToWindow, System.Windows.Threading.DispatcherPriority.Loaded);
            return;
        }

        var scale = Math.Min(viewportW / _naturalSize.Width, viewportH / _naturalSize.Height);
        if (scale <= 0 || double.IsInfinity(scale) || double.IsNaN(scale))
            scale = 1;
        // Fit never upscales beyond 100% for clearer defaults; View → 实际大小 can go 100%.
        // Actually user asked for fit - typical viewers allow fit that can be < 100%. Don't cap.
        ApplyScale(scale, keepFitFlag: true);
    }

    private void SetZoom(double zoom)
    {
        _fitToWindow = false;
        ApplyScale(zoom, keepFitFlag: false);
        UpdateZoomStatus();
    }

    private void ZoomBy(double factor)
    {
        if (PreviewImage.Source is null)
            return;

        var current = _fitToWindow ? EstimateCurrentFitZoom() : _zoom;
        SetZoom(current * factor);
    }

    private double EstimateCurrentFitZoom()
    {
        if (_naturalSize.Width <= 0 || _naturalSize.Height <= 0)
            return 1;
        var viewportW = Math.Max(1, ImageScroll.ViewportWidth);
        var viewportH = Math.Max(1, ImageScroll.ViewportHeight);
        return Math.Min(viewportW / _naturalSize.Width, viewportH / _naturalSize.Height);
    }

    private void ApplyScale(double zoom, bool keepFitFlag)
    {
        zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        _zoom = zoom;
        if (!keepFitFlag)
            _fitToWindow = false;

        ImageScale.ScaleX = zoom;
        ImageScale.ScaleY = zoom;
        UpdateZoomStatus();
    }

    private void UpdateZoomStatus()
    {
        if (PreviewImage.Source is null)
        {
            ZoomStatusText.Text = "缩放: —";
            return;
        }

        ZoomStatusText.Text = _fitToWindow
            ? $"缩放: 适应窗口 ({_zoom * 100:0}%)"
            : $"缩放: {_zoom * 100:0}%";
    }

    private void ImageScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (PreviewImage.Source is null)
            return;

        // Wheel zooms; Ctrl+Wheel also zooms (same behavior).
        e.Handled = true;
        ZoomBy(e.Delta > 0 ? ZoomStep : 1.0 / ZoomStep);
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        if (ctrl && e.Key == Key.O)
        {
            if (shift)
                PromptOpenFolder();
            else
                PromptOpenImage();
            e.Handled = true;
            return;
        }

        if (ctrl && (e.Key == Key.OemPlus || e.Key == Key.Add))
        {
            ZoomBy(ZoomStep);
            e.Handled = true;
            return;
        }

        if (ctrl && (e.Key == Key.OemMinus || e.Key == Key.Subtract))
        {
            ZoomBy(1.0 / ZoomStep);
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.D0)
        {
            SetFitToWindow();
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Left:
                Navigate(-1);
                e.Handled = true;
                break;
            case Key.Right:
                Navigate(1);
                e.Handled = true;
                break;
            case Key.Up:
            case Key.Down:
            case Key.PageUp:
            case Key.PageDown:
            case Key.Home:
            case Key.End:
            case Key.Space:
                // 吞掉滚动/导航键，避免未处理按键触发系统蜂鸣
                e.Handled = true;
                break;
            case Key.Escape:
                FileClose_Click(sender, e);
                e.Handled = true;
                break;
        }
    }

    private void Navigate(int delta)
    {
        if (_siblings.Count == 0)
            return;

        _index = (_index + delta) % _siblings.Count;
        if (_index < 0)
            _index += _siblings.Count;

        ShowCurrent();
    }
}
