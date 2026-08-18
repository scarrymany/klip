using System.Xml.Linq;

namespace Klip.Tests;

public sealed class ReleaseContractTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void GitHub_button_precedes_minimize_button_in_title_bar()
    {
        var xaml = Read("src", "Klip", "MainWindow.xaml");
        var github = xaml.IndexOf("Click=\"OnOpenGitHub\"", StringComparison.Ordinal);
        var minimize = xaml.IndexOf("Click=\"OnMinimize\"", StringComparison.Ordinal);

        Assert.True(github >= 0, "GitHub chrome button is missing.");
        Assert.True(github < minimize, "GitHub chrome button must precede minimize.");
        Assert.Contains("ToolTip=\"Репозиторий на GitHub\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Release_version_is_synchronized()
    {
        const string version = "1.1.1";
        var project = XDocument.Parse(Read("src", "Klip", "Klip.csproj"));
        var properties = project.Root!.Elements("PropertyGroup").Elements().ToDictionary(x => x.Name.LocalName, x => x.Value);

        Assert.Equal(version, properties["Version"]);
        Assert.Equal(version + ".0", properties["AssemblyVersion"]);
        Assert.Equal(version + ".0", properties["FileVersion"]);
        Assert.Equal(version, properties["InformationalVersion"]);
        Assert.Contains($"assemblyIdentity version=\"{version}.0\"", Read("src", "Klip", "app.manifest"), StringComparison.Ordinal);
        Assert.Contains($"#define MyAppVersion \"{version}\"", Read("installer", "klip.iss"), StringComparison.Ordinal);
        Assert.Contains($"ProductVersion = \"{version}\"", Read("installer", "Klip.wxs"), StringComparison.Ordinal);
        Assert.Contains($"## [{version}]", Read("CHANGELOG.md"), StringComparison.Ordinal);
        Assert.Contains($"Klip-Setup-{version}.exe", Read("README.md"), StringComparison.Ordinal);
        Assert.Contains($"Klip-Setup-{version}.exe", Read("README.en.md"), StringComparison.Ordinal);
    }

    [Fact]
    public void Release_body_uses_tag_version_output()
    {
        var workflow = Read(".github", "workflows", "release.yml");

        Assert.Contains("Klip-Setup-${{ needs.build.outputs.version }}.exe", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("Klip-Setup-1.1.1.exe", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_copy_describes_layered_transparency()
    {
        var xaml = Read("src", "Klip", "MainWindow.xaml");

        Assert.DoesNotContain("Mica и Acrylic на Windows 11", xaml, StringComparison.Ordinal);
        Assert.Contains("Одинаковый layered-режим на Windows 10 и 11", xaml, StringComparison.Ordinal);
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
