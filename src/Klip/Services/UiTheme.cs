using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Klip.Services;

public sealed class UiTheme
{
    public const string DefaultAccent = "#D7DDE6";
    public const string DefaultTint = "#0B0D11";

    public bool Acrylic { get; set; } = true;
    public string Accent { get; set; } = DefaultAccent;
    public string Tint { get; set; } = DefaultTint;
    public string? WallpaperFile { get; set; }
    public double Blur { get; set; } = 18;
    public double Dim { get; set; } = 0.58;
    public string Stretch { get; set; } = "UniformToFill";

    public static string WallpaperDirectory => ClipStore.DataDirectory;

    public string? WallpaperPath
        => string.IsNullOrWhiteSpace(WallpaperFile)
            ? null
            : Path.Combine(WallpaperDirectory, WallpaperFile);

    public static UiTheme Load(ClipStore store)
    {
        var theme = new UiTheme
        {
            Acrylic = store.GetSetting("ui.acrylic") != "0",
            Accent = store.GetSetting("ui.accent") ?? DefaultAccent,
            Tint = store.GetSetting("ui.tint") ?? DefaultTint,
            WallpaperFile = EmptyToNull(store.GetSetting("ui.wallpaper")),
            Stretch = store.GetSetting("ui.stretch") ?? "UniformToFill",
        };

        if (double.TryParse(store.GetSetting("ui.blur"), NumberStyles.Float, CultureInfo.InvariantCulture, out var blur))
            theme.Blur = Math.Clamp(blur, 0, 40);
        if (double.TryParse(store.GetSetting("ui.dim"), NumberStyles.Float, CultureInfo.InvariantCulture, out var dim))
            theme.Dim = Math.Clamp(dim, 0.15, 0.88);

        if (theme.WallpaperPath is { } path && !File.Exists(path))
            theme.WallpaperFile = null;

        return theme;
    }

    public void Save(ClipStore store)
    {
        store.SetSetting("ui.acrylic", Acrylic ? "1" : "0");
        store.SetSetting("ui.accent", Accent);
        store.SetSetting("ui.tint", Tint);
        store.SetSetting("ui.wallpaper", WallpaperFile ?? "");
        store.SetSetting("ui.stretch", Stretch);
        store.SetSetting("ui.blur", Blur.ToString("0.#", CultureInfo.InvariantCulture));
        store.SetSetting("ui.dim", Dim.ToString("0.##", CultureInfo.InvariantCulture));
    }

    public void Reset()
    {
        Acrylic = true;
        Accent = DefaultAccent;
        Tint = DefaultTint;
        WallpaperFile = null;
        Blur = 18;
        Dim = 0.58;
        Stretch = "UniformToFill";
    }

    public string InstallWallpaper(string sourcePath)
    {
        var ext = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(ext) || ext.Length > 8)
            ext = ".jpg";

        foreach (var leftover in Directory.EnumerateFiles(WallpaperDirectory, "wallpaper.*"))
        {
            try { File.Delete(leftover); } catch { /* in use */ }
        }

        var name = "wallpaper" + ext.ToLowerInvariant();
        var dest = Path.Combine(WallpaperDirectory, name);
        File.Copy(sourcePath, dest, overwrite: true);
        WallpaperFile = name;
        return dest;
    }

    public void ClearWallpaper()
    {
        if (WallpaperPath is { } path)
        {
            try { File.Delete(path); } catch { /* leftover */ }
        }

        WallpaperFile = null;
    }

    public static Color ContrastOn(Color accent)
    {
        var lum = (0.2126 * accent.R + 0.7152 * accent.G + 0.0722 * accent.B) / 255.0;
        return lum > 0.58
            ? Color.FromRgb(0x0B, 0x0D, 0x11)
            : Color.FromRgb(0xEE, 0xF1, 0xF5);
    }

    public static Color? TryParseHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return null;
        hex = hex.Trim().TrimStart('#');
        if (hex.Length != 6)
            return null;
        try
        {
            return Color.FromRgb(
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16));
        }
        catch
        {
            return null;
        }
    }

    public static string ToHex(Color color)
        => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static Color SampleAverage(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bmp.DecodePixelWidth = 48;
        bmp.UriSource = new Uri(path);
        bmp.EndInit();
        bmp.Freeze();

        var conv = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
        var w = conv.PixelWidth;
        var h = conv.PixelHeight;
        var pixels = new byte[w * h * 4];
        conv.CopyPixels(pixels, w * 4, 0);

        long r = 0, g = 0, b = 0;
        var n = w * h;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            b += pixels[i];
            g += pixels[i + 1];
            r += pixels[i + 2];
        }

        // Pull the average toward a dark readable base so cards stay legible.
        var mix = 0.28;
        return Color.FromRgb(
            (byte)(r / n * mix),
            (byte)(g / n * mix),
            (byte)(b / n * mix));
    }

    public static BitmapImage? LoadBitmap(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public static System.Windows.Media.Stretch ParseStretch(string? value) => value switch
    {
        "Fill" => System.Windows.Media.Stretch.Fill,
        "Uniform" => System.Windows.Media.Stretch.Uniform,
        _ => System.Windows.Media.Stretch.UniformToFill,
    };

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
