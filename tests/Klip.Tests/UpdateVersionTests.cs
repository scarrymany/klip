using Klip.Services;

namespace Klip.Tests;

public sealed class UpdateVersionTests
{
    [Theory]
    [InlineData("v1.1.0", 1, 1, 0)]
    [InlineData("1.0.10", 1, 0, 10)]
    [InlineData("V2.0.0", 2, 0, 0)]
    public void ParseTag_reads_semver(string tag, int major, int minor, int build)
    {
        var version = UpdateService.ParseTag(tag);
        Assert.NotNull(version);
        Assert.Equal(new Version(major, minor, build), version);
    }

    [Fact]
    public void ParseTag_rejects_garbage()
        => Assert.Null(UpdateService.ParseTag("latest"));
}
