using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Klip.Services;

public sealed class ClipboardWatcher : IDisposable
{
    public event Action<string>? TextCaptured;

    private HwndSource? _source;
    private bool _ignoreNext;
    private const int WmClipboardUpdate = 0x031D;

    public void Start(IntPtr hwnd)
    {
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(Hook);
        AddClipboardFormatListener(hwnd);
    }

    public void IgnoreNext() => _ignoreNext = true;

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmClipboardUpdate) return IntPtr.Zero;
        if (_ignoreNext)
        {
            _ignoreNext = false;
            return IntPtr.Zero;
        }

        try
        {
            if (System.Windows.Clipboard.ContainsText())
            {
                var text = System.Windows.Clipboard.GetText();
                if (!string.IsNullOrWhiteSpace(text))
                    TextCaptured?.Invoke(text);
            }
        }
        catch (COMException)
        {
            // clipboard busy
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_source is not null)
        {
            RemoveClipboardFormatListener(_source.Handle);
            _source.RemoveHook(Hook);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
}
