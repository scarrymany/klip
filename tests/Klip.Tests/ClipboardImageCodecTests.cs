using System.Windows.Media;
using System.Windows.Media.Imaging;
using Klip.Services;

namespace Klip.Tests;

public sealed class ClipboardImageCodecTests
{
    [Fact]
    public void Encodes_bitmap_as_png_and_preserves_dimensions()
    {
        var pixels = new byte[]
        {
            0x10, 0x20, 0x30, 0xFF,
            0x40, 0x50, 0x60, 0xFF,
        };
        var bitmap = BitmapSource.Create(
            2,
            1,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            8);

        var image = ClipboardImageCodec.EncodePng(bitmap);

        Assert.Equal(2, image.Width);
        Assert.Equal(1, image.Height);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, image.PngBytes[..4]);
    }

    [Fact]
    public void Loads_png_without_holding_source_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"klip-{Guid.NewGuid():N}.png");
        try
        {
            var bitmap = BitmapSource.Create(
                1,
                1,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                new byte[] { 0x10, 0x20, 0x30, 0xFF },
                4);
            File.WriteAllBytes(path, ClipboardImageCodec.EncodePng(bitmap).PngBytes);

            var loaded = ClipboardImageCodec.LoadPng(path);
            File.Delete(path);

            Assert.Equal(1, loaded.PixelWidth);
            Assert.Equal(1, loaded.PixelHeight);
            Assert.True(loaded.IsFrozen);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
