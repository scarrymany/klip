namespace Klip.Services;

public static class WindowPlacement
{
    public const double MinVisible = 80;

    public static bool TryNormalize(
        ref double left,
        ref double top,
        ref double width,
        ref double height,
        double minWidth,
        double minHeight,
        double screenLeft,
        double screenTop,
        double screenWidth,
        double screenHeight)
    {
        if (width < minWidth || height < minHeight)
            return false;
        if (screenWidth < 1 || screenHeight < 1)
            return false;

        width = Math.Min(width, screenWidth);
        height = Math.Min(height, screenHeight);

        var maxLeft = screenLeft + screenWidth - MinVisible;
        var maxTop = screenTop + screenHeight - MinVisible;
        var minLeft = screenLeft + MinVisible - width;
        var minTop = screenTop;

        if (left > maxLeft)
            left = maxLeft;
        if (left < minLeft)
            left = minLeft;
        if (top > maxTop)
            top = maxTop;
        if (top < minTop)
            top = minTop;

        return true;
    }
}
