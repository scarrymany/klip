using System.Windows.Interop;
using Klip.Native;

namespace Klip.Services;

public sealed class HotkeyService : IDisposable
{
    private HwndSource? _source;
    private bool _registered;

    public event EventHandler? Activated;

    public bool IsRegistered => _registered;

    public bool Attach(HwndSource source)
    {
        Detach();
        _source = source;
        _source.AddHook(Hook);

        _registered = NativeMethods.RegisterHotKey(
            source.Handle,
            NativeMethods.HotkeyId,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT,
            NativeMethods.VK_V);

        if (!_registered)
        {
            _registered = NativeMethods.RegisterHotKey(
                source.Handle,
                NativeMethods.HotkeyId,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
                NativeMethods.VK_V);
        }

        return _registered;
    }

    public void Detach()
    {
        if (_source is not null)
        {
            try
            {
                NativeMethods.UnregisterHotKey(_source.Handle, NativeMethods.HotkeyId);
            }
            catch
            {
                // HWND may already be gone.
            }

            _source.RemoveHook(Hook);
            _source = null;
        }

        _registered = false;
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == NativeMethods.HotkeyId)
        {
            Activated?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose() => Detach();
}
