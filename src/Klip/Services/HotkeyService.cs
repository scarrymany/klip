using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Klip.Services;

public sealed class HotkeyService : IDisposable
{
    public event Action? Pressed;

    private HwndSource? _source;
    private const int HotkeyId = 0x4B4C; // KL
    private const int WmHotkey = 0x0312;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint VkV = 0x56;

    public bool Register(IntPtr hwnd)
    {
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(Hook);
        return RegisterHotKey(hwnd, HotkeyId, ModControl | ModShift, VkV);
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_source is not null)
        {
            UnregisterHotKey(_source.Handle, HotkeyId);
            _source.RemoveHook(Hook);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
