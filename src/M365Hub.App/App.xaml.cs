using Microsoft.UI.Xaml;

namespace M365Hub.App;

/// <summary>Application entry. The window is created in MainWindow; the
/// gateway is started from there so the startup screen can show progress.</summary>
public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
