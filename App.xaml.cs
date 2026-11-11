using System.Windows;

namespace NexaTextGrab;

public partial class App : Application
{
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _mainWindow = new MainWindow();
        _mainWindow.Initialize(e.Args);
        _mainWindow.Show();
    }

}
