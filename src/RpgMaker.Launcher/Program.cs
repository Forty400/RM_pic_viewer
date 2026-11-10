using System.Diagnostics;
using System.Text;
using System.Windows.Forms;

// Thin root entry so install folder only shows a clear .exe (real app lives in bin\).
// Use ProcessPath directory — AppContext.BaseDirectory can be a temp extract folder for single-file.
var processPath = Environment.ProcessPath
    ?? throw new InvalidOperationException("Cannot resolve launcher path.");
var root = Path.GetDirectoryName(processPath)
    ?? throw new InvalidOperationException("Cannot resolve install directory.");
var appExe = Path.Combine(root, "bin", "RpgMakerImageViewer.exe");

if (!File.Exists(appExe))
{
    MessageBox.Show(
        "找不到程序主体：\n" + appExe + "\n\n请重新安装，或确认未移动/删除 bin 文件夹。",
        "RPG Maker 图片阅读器",
        MessageBoxButtons.OK,
        MessageBoxIcon.Error);
    return 1;
}

var cliArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
var start = new ProcessStartInfo
{
    FileName = appExe,
    WorkingDirectory = Path.GetDirectoryName(appExe)!,
    UseShellExecute = false
};

if (cliArgs.Length > 0)
{
    var sb = new StringBuilder();
    foreach (var a in cliArgs)
    {
        if (sb.Length > 0) sb.Append(' ');
        sb.Append('"').Append(a.Replace("\"", "\\\"")).Append('"');
    }

    start.Arguments = sb.ToString();
}

try
{
    using var process = Process.Start(start);
    if (process is null)
    {
        MessageBox.Show("无法启动程序主体。", "RPG Maker 图片阅读器",
            MessageBoxButtons.OK, MessageBoxIcon.Error);
        return 3;
    }

    return 0;
}
catch (Exception ex)
{
    MessageBox.Show(
        "启动失败：\n" + ex.Message,
        "RPG Maker 图片阅读器",
        MessageBoxButtons.OK,
        MessageBoxIcon.Error);
    return 2;
}
