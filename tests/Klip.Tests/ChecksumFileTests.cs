using Klip.Services;

namespace Klip.Tests;

public sealed class ChecksumFileTests
{
    private const string SetupHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string ZipHash = "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";

    [Fact]
    public void Finds_text_mode_hash()
    {
        var text = SetupHash + "  Klip-Setup-1.1.0.exe\n";
        Assert.Equal(SetupHash, ChecksumFile.FindHash(text, "Klip-Setup-1.1.0.exe"));
    }

    [Fact]
    public void Finds_git_bash_asterisk_hash()
    {
        var text = SetupHash + " *Klip-Setup-1.1.0.exe\n";
        Assert.Equal(SetupHash, ChecksumFile.FindHash(text, "Klip-Setup-1.1.0.exe"));
    }

    [Fact]
    public void Ignores_other_files()
    {
        var text = SetupHash + "  other.exe\n" + ZipHash + "  Klip-Portable-win-x64.zip\n";
        Assert.Equal(ZipHash, ChecksumFile.FindHash(text, "Klip-Portable-win-x64.zip"));
        Assert.Null(ChecksumFile.FindHash(text, "missing.exe"));
    }

    [Fact]
    public void Strips_bom()
    {
        var text = "\uFEFF" + SetupHash + "  SHA256SUMS.txt\n";
        Assert.Equal(SetupHash, ChecksumFile.FindHash(text, "SHA256SUMS.txt"));
    }

    [Fact]
    public void Rejects_short_hash()
    {
        Assert.Null(ChecksumFile.FindHash("deadbeef  file.exe\n", "file.exe"));
    }
}
