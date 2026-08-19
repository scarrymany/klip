using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Klip.Native;

namespace Klip.Services;

public sealed class ClipboardWatcher : IDisposable
{
    private HwndSource? _source;
    private int _ignoreOwn;
    private int _epoch;

    public event EventHandler<string>? TextCaptured;

    public event EventHandler<ClipboardImageData>? ImageCaptured;

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

    public void CopyImage(string path)
    {
        var image = ClipboardImageCodec.LoadPng(path);
        IgnoreOwnCopy();
        try
        {
            var data = new DataObject();
            data.SetImage(image);
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

        var epoch = Interlocked.Increment(ref _epoch);
        _ = CaptureAsync(epoch);
        return IntPtr.Zero;
    }

    private async Task CaptureAsync(int epoch)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            if (epoch != Volatile.Read(ref _epoch))
                return;

            try
            {
                if (System.Windows.Clipboard.ContainsImage())
                {
                    var bitmap = System.Windows.Clipboard.GetImage();
                    if (bitmap is null)
                        return;
                    var frozen = bitmap.Clone();
                    frozen.Freeze();
                    var image = await Task.Run(() => ClipboardImageCodec.EncodePng(frozen)).ConfigureAwait(true);
                    if (epoch != Volatile.Read(ref _epoch))
                        return;
                    ImageCaptured?.Invoke(this, image);
                    return;
                }

                if (!System.Windows.Clipboard.ContainsText())
                    return;

                var text = System.Windows.Clipboard.GetText(TextDataFormat.UnicodeText);
                if (epoch != Volatile.Read(ref _epoch))
                    return;
                if (!string.IsNullOrWhiteSpace(text))
                    TextCaptured?.Invoke(this, text);
                return;
            }
            catch (COMException)
            {
                await Task.Delay(40).ConfigureAwait(true);
            }
            catch (Exception)
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        Interlocked.Increment(ref _epoch);
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
