using Klip.Models;

namespace Klip.Tests;

public sealed class ClipKindTests
{
    [Fact]
    public void Empty_is_clip()
        => Assert.Equal(ClipKinds.Clip, ClipKinds.Detect("   "));

    [Fact]
    public void Single_url_is_link()
        => Assert.Equal(ClipKinds.Link, ClipKinds.Detect("https://example.com/path"));

    [Fact]
    public void Url_with_newline_is_not_link()
        => Assert.NotEqual(ClipKinds.Link, ClipKinds.Detect("https://example.com\nmore"));

    [Fact]
    public void Code_block_is_code()
    {
        var text = "function hello() {\n  const x = 1;\n  return x;\n}\n";
        Assert.Equal(ClipKinds.Code, ClipKinds.Detect(text));
    }

    [Fact]
    public void Long_text_is_note()
        => Assert.Equal(ClipKinds.Note, ClipKinds.Detect(new string('a', 400)));

    [Fact]
    public void Short_plain_text_is_clip()
        => Assert.Equal(ClipKinds.Clip, ClipKinds.Detect("hello world"));
}
