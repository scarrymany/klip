using Klip.Services;
using Microsoft.Data.Sqlite;

namespace Klip.Tests;

public sealed class UiThemeTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "KlipTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void New_profile_uses_the_solid_Scarp_palette()
    {
        using var store = OpenStore();

        var theme = UiTheme.Load(store);

        Assert.False(theme.Acrylic);
        Assert.Equal("#FFFFFF", theme.Accent);
        Assert.Equal("#0A0A0A", theme.Tint);
    }

    [Fact]
    public void Saved_appearance_remains_compatible_after_restart()
    {
        using (var store = OpenStore())
        {
            new UiTheme
            {
                Acrylic = true,
                Accent = "#D7DDE6",
                Tint = "#0B0D11",
                Blur = 12,
                Dim = 0.7,
                Stretch = "Uniform",
            }.Save(store);
        }

        using (var store = OpenStore())
        {
            var theme = UiTheme.Load(store);
            Assert.True(theme.Acrylic);
            Assert.Equal("#D7DDE6", theme.Accent);
            Assert.Equal("#0B0D11", theme.Tint);
            Assert.Equal(12, theme.Blur);
            Assert.Equal(0.7, theme.Dim);
            Assert.Equal("Uniform", theme.Stretch);
        }
    }

    [Fact]
    public void Reset_replaces_custom_appearance_with_Scarp_defaults()
    {
        var theme = new UiTheme { Acrylic = true, Accent = "#7AA2C4", Tint = "#12151C", WallpaperFile = "wallpaper.png" };

        theme.Reset();

        Assert.False(theme.Acrylic);
        Assert.Equal("#FFFFFF", theme.Accent);
        Assert.Equal("#0A0A0A", theme.Tint);
        Assert.Null(theme.WallpaperFile);
    }

    private ClipStore OpenStore() => new(Path.Combine(_directory, "klip.db"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
