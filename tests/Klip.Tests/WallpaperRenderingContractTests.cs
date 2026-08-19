namespace Klip.Tests;

public sealed class WallpaperRenderingContractTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void Blur_transitions_detach_zero_and_attach_positive_rendering()
    {
        var xaml = Read("src", "Klip", "MainWindow.xaml");
        var code = Read("src", "Klip", "MainWindow.xaml.cs");

        Assert.DoesNotContain("<Image.CacheMode>", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Image.Effect>", xaml, StringComparison.Ordinal);
        Assert.Contains("if (radius <= 0)", code, StringComparison.Ordinal);
        Assert.Contains("WallpaperImage.Effect = null;", code, StringComparison.Ordinal);
        Assert.Contains("WallpaperImage.CacheMode = null;", code, StringComparison.Ordinal);
        Assert.Contains("ApplyWallpaperBlur(_theme.Blur);", code, StringComparison.Ordinal);
        Assert.Contains("_wallpaperBlurEffect.Radius = radius;", code, StringComparison.Ordinal);
        Assert.Contains("WallpaperImage.CacheMode = _wallpaperBitmapCache;", code, StringComparison.Ordinal);
        Assert.Contains("WallpaperImage.Effect = _wallpaperBlurEffect;", code, StringComparison.Ordinal);
        Assert.Matches(
            @"if \(radius <= 0\)\s*\{\s*WallpaperImage\.Effect = null;\s*WallpaperImage\.CacheMode = null;\s*return;\s*\}",
            code);
    }

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine([Root, .. parts]));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Klip.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
