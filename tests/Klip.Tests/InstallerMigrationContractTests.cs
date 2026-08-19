namespace Klip.Tests;

public sealed class InstallerMigrationContractTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void Inno_setup_removes_related_msi_and_legacy_x86_files()
    {
        var script = Read("installer", "klip.iss");

        Assert.Contains("MsiEnumRelatedProductsW@msi.dll", script, StringComparison.Ordinal);
        Assert.Contains("{9C4E2B71-6A18-4F3D-8E05-B2D47C91A6F3}", script, StringComparison.Ordinal);
        Assert.Contains("/x \"", script, StringComparison.Ordinal);
        Assert.Contains("CurStep = ssPostInstall", script, StringComparison.Ordinal);
        Assert.Contains("DelTree(ExpandConstant('{pf32}\\Klip')", script, StringComparison.Ordinal);
        Assert.Contains("DelTree(ExpandConstant('{commonprograms}\\Klip')", script, StringComparison.Ordinal);
        Assert.Contains("LegacyMsiMaximumVersion = '1.2.1'", script, StringComparison.Ordinal);
        Assert.Contains("ErrorNoMoreItems = 259", script, StringComparison.Ordinal);
        Assert.Contains("MsiGetProductInfoW@msi.dll", script, StringComparison.Ordinal);
        Assert.Contains("'Version',", script, StringComparison.Ordinal);
        Assert.Contains("RawVersion shr 24", script, StringComparison.Ordinal);
        Assert.Contains("ComparePackedVersion(InstalledVersion, SetupVersion) > 0", script, StringComparison.Ordinal);
        Assert.Contains("Result := FindLegacyMsiProducts()", script, StringComparison.Ordinal);
        Assert.Contains("function NeedRestart(): Boolean", script, StringComparison.Ordinal);
        Assert.Contains("LegacyMsiRestartRequired := True", script, StringComparison.Ordinal);
        Assert.DoesNotContain("[InstallDelete]", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Release_builds_msi_as_x64()
    {
        var workflow = Read(".github", "workflows", "release.yml");
        var wixBuild = workflow.IndexOf("wix build installer\\Klip.wxs", StringComparison.Ordinal);
        var wixOutput = workflow.IndexOf("-o \"dist\\Klip-", wixBuild, StringComparison.Ordinal);
        var command = workflow[wixBuild..wixOutput];

        Assert.Contains("-arch x64", command, StringComparison.Ordinal);

        var wix = Read("installer", "Klip.wxs");
        Assert.DoesNotContain("C8A1D4E2-7B35-4F09-9A16-5E3C8D2B7041", wix, StringComparison.Ordinal);
        Assert.DoesNotContain("A9C3E5F1-8D24-4B70-91E6-3F5A7C2D9048", wix, StringComparison.Ordinal);
        Assert.DoesNotContain("D4B7E1A9-2C68-4E15-8F30-6A9D1C5B8342", wix, StringComparison.Ordinal);
    }

    [Fact]
    public void Manual_release_uses_project_version()
    {
        var workflow = Read(".github", "workflows", "release.yml");

        Assert.Contains("project_version=", workflow, StringComparison.Ordinal);
        Assert.Contains("version=\"${project_version}\"", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("version=1.0.0", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_repairs_existing_run_entry_to_current_executable()
    {
        var startup = Read("src", "Klip", "Services", "StartupService.cs");
        var app = Read("src", "Klip", "App.xaml.cs");

        Assert.Contains("public static void RepairIfEnabled()", startup, StringComparison.Ordinal);
        Assert.Contains("StartupService.RepairIfEnabled();", app, StringComparison.Ordinal);
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
