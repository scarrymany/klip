using Klip.Services;

namespace Klip.Tests;

public sealed class ChecksumFileTests
{
    [Fact]
    public void Finds_text_mode_hash()
    {
        const string text = "abc123def456  Klip-Setup-1.1.0.exe\n";
        Assert.Equal("abc123def456", ChecksumFile.FindHash(text, "Klip-Setup-1.1.0.exe"));
    }

    [Fact]
    public void Finds_git_bash_asterisk_hash()
    {
        const string text = "abc123def456 *Klip-Setup-1.1.0.exe\n";
        Assert.Equal("abc123def456", ChecksumFile.FindHash(text, "Klip-Setup-1.1.0.exe"));
    }

    [Fact]
    public void Ignores_other_files()
    {
        const string text = "aaa  other.exe\nbbb  Klip-Portable-win-x64.zip\n";
        Assert.Equal("bbb", ChecksumFile.FindHash(text, "Klip-Portable-win-x64.zip"));
        Assert.Null(ChecksumFile.FindHash(text, "missing.exe"));
    }

    [Fact]
    public void Strips_bom()
    {
        var text = "\uFEFFdeadbeef  SHA256SUMS.txt\n";
        Assert.Equal("deadbeef", ChecksumFile.FindHash(text, "SHA256SUMS.txt"));
    }
}
