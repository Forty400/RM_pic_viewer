using System.IO;
using System.Windows;

using RpgMaker.Install;

namespace RpgMaker.Setup;

public partial class App : System.Windows.Application
{
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        var uninstallByArgs = e.Args.Any(a =>
            a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/u", StringComparison.OrdinalIgnoreCase));

        // Double-clicking Uninstall.exe should always enter uninstall mode (no args needed).
        var exeName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "");
        var uninstallByName = exeName.Equals("Uninstall", StringComparison.OrdinalIgnoreCase);

        var window = new MainWindow(uninstallMode: uninstallByArgs || uninstallByName);
        window.Show();
    }
}
