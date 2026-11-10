using System.Drawing;
using Forms = System.Windows.Forms;

namespace RpgMaker.Viewer;

internal sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private bool _disposed;

    public event Action? OpenImageRequested;
    public event Action? ShowWindowRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public TrayIconService()
    {
        _icon = CreateTrayIcon();
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = "RPG Maker 图片阅读器",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _notifyIcon.DoubleClick += (_, _) => ShowWindowRequested?.Invoke();
    }

    public void ShowRunningTip()
    {
        // 不弹气泡：避免系统通知音；托盘图标本身已足够提示。
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开图片…", null, (_, _) => OpenImageRequested?.Invoke());
        menu.Items.Add("显示窗口", null, (_, _) => ShowWindowRequested?.Invoke());
        menu.Items.Add("设置…", null, (_, _) => SettingsRequested?.Invoke());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出（释放内存）", null, (_, _) => ExitRequested?.Invoke());
        return menu;
    }

    private static Icon CreateTrayIcon()
    {
        using var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(30, 30, 30));
        using var brush = new SolidBrush(Color.FromArgb(80, 180, 120));
        g.FillRectangle(brush, 2, 2, 12, 12);
        using var pen = new Pen(Color.White, 1);
        g.DrawRectangle(pen, 3, 3, 9, 9);
        var hIcon = bmp.GetHicon();
        return (Icon)Icon.FromHandle(hIcon).Clone();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon.Dispose();
    }
}
