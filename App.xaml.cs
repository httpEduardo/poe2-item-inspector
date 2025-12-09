using System.Windows;

namespace PoE2Inspector;

public partial class App : Application
{
    private AppHost? _host;
    private MainWindow? _mainWindow;

    public static MainWindow? MainWindowInstance { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        _mainWindow = new MainWindow();
        MainWindowInstance = _mainWindow;
        _mainWindow.Show();

        _host = new AppHost();
        _host.Start(_mainWindow);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Stop();
        base.OnExit(e);
    }
}
