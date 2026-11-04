using System.Windows;

namespace DeviceWorkbench.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
