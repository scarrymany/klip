using System.IO;
using System.Windows.Media.Imaging;

namespace Klip.Services;

public sealed class ClipboardImageData(byte[] pngBytes, int width, int height) : EventArgs
{
    public byte[] PngBytes { get; } = pngBytes;

    public int Width { get; } = width;

    public int Height { get; } = height;
}

public static class ClipboardImageCodec
{
    public static ClipboardImageData EncodePng(BitmapSource bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
            throw new ArgumentException("Изображение имеет недопустимый размер.", nameof(bitmap));

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return new ClipboardImageData(stream.ToArray(), bitmap.PixelWidth, bitmap.PixelHeight);
    }

    public static BitmapSource LoadPng(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = new PngBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var bitmap = BitmapFrame.Create(decoder.Frames[0]);
        bitmap.Freeze();
        return bitmap;
    }
}
