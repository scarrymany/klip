using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Klip.Native;

namespace Klip.Services;

public sealed class ClipboardWatcher : IDisposable
{
    private HwndSource? _source;
    private int _ignoreOwn;

    public event EventHandler<string>? TextCaptured;

    public HwndSource? Source => _source;

    public void Start()
    {
        if (_source is not null)
            return;

        var parameters = new HwndSourceParameters("Klip.Clipboard")
        {
            Width = 0,
            Height = 0,
            PositionX = -32000,
            PositionY = -32000,
            WindowStyle = unchecked((int)0x80000000),
        };

        _source = new HwndSource(parameters);
        _source.AddHook(Hook);

        if (!NativeMethods.AddClipboardFormatListener(_source.Handle))
        {
            throw new InvalidOperationException(
                "Не удалось подписаться на буфер обмена (AddClipboardFormatListener).");
        }
    }

    public void IgnoreOwnCopy() => Interlocked.Exchange(ref _ignoreOwn, 1);

    public void CopyText(string text)
    {
        IgnoreOwnCopy();
        try
        {
            var data = new DataObject();
            data.SetData(DataFormats.UnicodeText, text);
            System.Windows.Clipboard.SetDataObject(data, copy: true);
        }
        catch
        {
            Interlocked.Exchange(ref _ignoreOwn, 0);
            throw;
        }
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WM_CLIPBOARDUPDATE)
            return IntPtr.Zero;

        if (Interlocked.Exchange(ref _ignoreOwn, 0) == 1)
            return IntPtr.Zero;

        var text = TryReadText();
        if (!string.IsNullOrWhiteSpace(text))
            TextCaptured?.Invoke(this, text);

        return IntPtr.Zero;
    }

    private static string? TryReadText()
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                if (!System.Windows.Clipboard.ContainsText())
                    return null;

                var text = System.Windows.Clipboard.GetText(TextDataFormat.UnicodeText);
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
            catch (COMException)
            {
                Thread.Sleep(40);
            }
            catch (Exception)
            {
                return null;
            }
        }

        return null;
    }

    public void Dispose()
    {
        if (_source is null)
            return;

        try
        {
            NativeMethods.RemoveClipboardFormatListener(_source.Handle);
        }
        catch
        {
            // HWND may already be gone.
        }

        _source.RemoveHook(Hook);
        _source.Dispose();
        _source = null;
    }
}
