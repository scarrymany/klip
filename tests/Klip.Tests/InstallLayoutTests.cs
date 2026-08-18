using Klip.Services;

namespace Klip.Tests;

public sealed class InstallLayoutTests
{
    [Fact]
    public void Marker_means_installed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "KlipTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.True(InstallLayout.IsPortableDirectory(dir));
            File.WriteAllText(Path.Combine(dir, InstallLayout.MarkerFileName), "1");
            Assert.False(InstallLayout.IsPortableDirectory(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Inno_uninstaller_means_installed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "KlipTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, InstallLayout.InnoUninstaller), "x");
            Assert.False(InstallLayout.IsPortableDirectory(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
