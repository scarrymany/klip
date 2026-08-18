using Klip.Models;

namespace Klip.Tests;

public sealed class ClipItemFormatTests
{
    [Theory]
    [InlineData(1, "1 копия")]
    [InlineData(2, "2 копии")]
    [InlineData(5, "5 копий")]
    [InlineData(11, "11 копий")]
    [InlineData(21, "21 копия")]
    [InlineData(22, "22 копии")]
    public void FormatCopies_uses_russian_plural(int count, string expected)
        => Assert.Equal(expected, ClipItem.FormatCopies(count));

    [Fact]
    public void PreviewText_collapses_whitespace_and_trims()
        => Assert.Equal("one two", ClipItem.PreviewText("one   \n two", 20));

    [Fact]
    public void PreviewText_adds_ellipsis_when_long()
    {
        var preview = ClipItem.PreviewText("abcdefghij", 6);
        Assert.EndsWith("…", preview, StringComparison.Ordinal);
        Assert.True(preview.Length <= 7);
    }
}
