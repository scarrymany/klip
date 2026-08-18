using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Klip.Native;

namespace Klip.Services;

public static class AcrylicHelper
{
    public static readonly Color SolidFallback = Color.FromRgb(0x0B, 0x0D, 0x11);
    public static readonly Brush SolidFallbackBrush = CreateSolid();

    // Layered WPF window. DWM Mica/Acrylic stay off so rounded corners
    // stay see-through instead of sitting on a white Win32 fill.
    public static bool Apply(Window window)
    {
        PrepareLayered(window);
        window.Background = System.Windows.Media.Brushes.Transparent;
        return NativeMethods.IsWindows11();
    }

    public static void RemoveBackdrop(Window window, Color? solid = null)
    {
        PrepareLayered(window);
        window.Background = System.Windows.Media.Brushes.Transparent;
    }

    public static bool TryApply(Window window) => Apply(window);

    public static bool IsWindows11() => NativeMethods.IsWindows11();

    private static void PrepareLayered(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var source = HwndSource.FromHwnd(hwnd);
        if (source?.CompositionTarget is { } target)
            target.BackgroundColor = Colors.Transparent;

        var dark = 1;
        NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref dark, sizeof(int));

        var none = NativeMethods.DWMSBT_NONE;
        NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int));
        var micaOff = 0;
        NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_MICA_EFFECT, ref micaOff, sizeof(int));

        var margins = new NativeMethods.Margins();
        NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins);

        NativeMethods.SetWindowRgn(hwnd, IntPtr.Zero, true);
        var square = NativeMethods.DWMWCP_DONOTROUND;
        NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref square, sizeof(int));
    }

    private static SolidColorBrush CreateSolid()
    {
        var brush = new SolidColorBrush(SolidFallback);
        brush.Freeze();
        return brush;
    }
}
