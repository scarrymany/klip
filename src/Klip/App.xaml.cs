using System.Windows;
using Klip.Services;

namespace Klip;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;
    private TrayService? _tray;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _mutex = new Mutex(true, @"Local\Klip.Clipboard.SingleInstance", out var created);
        if (!created)
        {
            Shutdown();
            return;
        }

        _window = new MainWindow();
        _tray = new TrayService();
        _tray.ShowClicked += () => _window.Reveal();
        _tray.ExitClicked += ShutdownApp;
        _window.Show();
    }

    private void ShutdownApp()
    {
        _tray?.Dispose();
        _window?.PrepareExit();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
