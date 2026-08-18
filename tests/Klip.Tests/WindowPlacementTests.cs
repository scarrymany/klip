using Klip.Services;

namespace Klip.Tests;

public sealed class WindowPlacementTests
{
    [Fact]
    public void Rejects_too_small_window()
    {
        double left = 10, top = 10, width = 20, height = 20;
        Assert.False(WindowPlacement.TryNormalize(ref left, ref top, ref width, ref height, 100, 100, 0, 0, 1920, 1080));
    }

    [Fact]
    public void Pulls_window_back_onto_virtual_screen()
    {
        double left = 8000, top = 4000, width = 940, height = 640;
        Assert.True(WindowPlacement.TryNormalize(ref left, ref top, ref width, ref height, 740, 500, 0, 0, 1920, 1080));
        Assert.True(left + WindowPlacement.MinVisible <= 1920);
        Assert.True(top + WindowPlacement.MinVisible <= 1080);
        Assert.True(top >= 0);
    }

    [Fact]
    public void Leaves_visible_window_in_place()
    {
        double left = 100, top = 80, width = 940, height = 640;
        Assert.True(WindowPlacement.TryNormalize(ref left, ref top, ref width, ref height, 740, 500, 0, 0, 1920, 1080));
        Assert.Equal(100, left);
        Assert.Equal(80, top);
    }
}
