using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Klip.Native;
using Klip.Services;

namespace Klip;

public partial class App : System.Windows.Application
{
    public const string MutexName = @"Local\Klip.scarrymany.single";
    public const string ShowMessageName = "Klip.scarrymany.Show";

    private Mutex? _mutex;
    private uint _showMessage;
    private ClipStore? _store;
    private ClipboardWatcher? _watcher;
    private HotkeyService? _hotkey;
    private TrayService? _tray;
    private MainWindow? _window;
    private bool _exitRequested;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var ru = CultureInfo.GetCultureInfo("ru-RU");
        CultureInfo.DefaultThreadCurrentCulture = ru;
        CultureInfo.DefaultThreadCurrentUICulture = ru;

        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.Default;

        _mutex = new Mutex(initiallyOwned: true, MutexName, out var created);
        _showMessage = NativeMethods.RegisterWindowMessage(ShowMessageName);

        if (!created)
        {
            NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST, _showMessage, IntPtr.Zero, IntPtr.Zero);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            System.Windows.MessageBox.Show(
                args.Exception.Message,
                "Клип",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        };

        try
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
        }
        catch
        {
            // Visual styles are optional for the tray menu.
        }

        try
        {
            _store = new ClipStore();
            _watcher = new ClipboardWatcher();
            _hotkey = new HotkeyService();
            _tray = new TrayService();

            _window = new MainWindow(_store, _watcher);
            MainWindow = _window;

            _watcher.Start();
            _watcher.TextCaptured += OnClipboardText;

            if (_watcher.Source is HwndSource source)
            {
                source.AddHook(SingleInstanceHook);
                if (!_hotkey.Attach(source))
                    _window.Notify("Горячая клавиша Ctrl+Shift+V занята");
            }

            _hotkey.Activated += (_, _) => Dispatcher.Invoke(ToggleWindow);
            _tray.ShowRequested += (_, _) => Dispatcher.Invoke(RevealWindow);
            _tray.ExitRequested += (_, _) => Dispatcher.Invoke(RequestExit);

            _window.Show();
            RestoreWindowBounds();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "Клип", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void OnClipboardText(object? sender, string text)
    {
        void Apply()
        {
            try
            {
                var added = _store?.TryAddFromClipboard(text);
                if (added is not null)
                    _window?.AddCaptured(added);
            }
            catch (Exception ex)
            {
                _window?.Notify(ex.Message);
            }
        }

        if (Dispatcher.CheckAccess())
            Apply();
        else
            Dispatcher.BeginInvoke(Apply);
    }

    private IntPtr SingleInstanceHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg == _showMessage)
        {
            Dispatcher.Invoke(RevealWindow);
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void ToggleWindow()
    {
        if (_window is null)
            return;

        if (_window.IsVisible && _window.IsActive)
            _window.HideToTray();
        else
            RevealWindow();
    }

    public void RevealWindow()
    {
        if (_window is null)
            return;

        _window.Reveal();
        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd != IntPtr.Zero)
        {
            if (NativeMethods.IsIconic(hwnd))
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
            NativeMethods.SetForegroundWindow(hwnd);
        }
    }

    public void RequestExit()
    {
        _exitRequested = true;
        PersistWindowBounds();
        _window?.ForceClose();
        Shutdown();
    }

    public bool ShouldCloseToTray => !_exitRequested;

    private void RestoreWindowBounds()
    {
        if (_window is null || _store is null)
            return;

        if (!double.TryParse(_store.GetSetting("window.left"), NumberStyles.Float, CultureInfo.InvariantCulture, out var left) ||
            !double.TryParse(_store.GetSetting("window.top"), NumberStyles.Float, CultureInfo.InvariantCulture, out var top) ||
            !double.TryParse(_store.GetSetting("window.width"), NumberStyles.Float, CultureInfo.InvariantCulture, out var width) ||
            !double.TryParse(_store.GetSetting("window.height"), NumberStyles.Float, CultureInfo.InvariantCulture, out var height))
        {
            return;
        }

        if (!WindowPlacement.TryNormalize(
                ref left,
                ref top,
                ref width,
                ref height,
                _window.MinWidth,
                _window.MinHeight,
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight))
        {
            return;
        }

        _window.WindowStartupLocation = WindowStartupLocation.Manual;
        _window.Left = left;
        _window.Top = top;
        _window.Width = width;
        _window.Height = height;
    }

    private void PersistWindowBounds()
    {
        if (_window is null || _store is null)
            return;

        if (_window.WindowState != WindowState.Normal)
            return;

        _store.SetSetting("window.left", _window.Left.ToString(CultureInfo.InvariantCulture));
        _store.SetSetting("window.top", _window.Top.ToString(CultureInfo.InvariantCulture));
        _store.SetSetting("window.width", _window.Width.ToString(CultureInfo.InvariantCulture));
        _store.SetSetting("window.height", _window.Height.ToString(CultureInfo.InvariantCulture));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        PersistWindowBounds();
        _hotkey?.Dispose();
        _watcher?.Dispose();
        _tray?.Dispose();
        _store?.Dispose();
        if (_mutex is not null)
        {
            try { _mutex.ReleaseMutex(); } catch { /* already released */ }
            _mutex.Dispose();
        }

        base.OnExit(e);
    }
}
