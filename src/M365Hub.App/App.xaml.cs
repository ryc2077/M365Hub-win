using Microsoft.UI.Xaml;
using System.IO;

namespace M365Hub.App;

/// <summary>Application entry. The window is created in MainWindow; the
/// gateway is started from there so the startup screen can show progress.</summary>
public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        // 未处理异常写崩溃日志（与安卓的 crash.log 同款），否则 WinUI
        // 只剩一个 0xC000027B 退出码、无从查起。
        UnhandledException += (_, e) =>
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "M365Hub");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "app-crash.log"),
                    DateTimeOffset.Now.ToString("o") + "\n" + e.Exception + "\n");
            }
            catch { }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
