using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Klip.Native;

namespace Klip.Services;

public static class AcrylicHelper
{
    public static readonly Color SolidFallback = Color.FromRgb(0x0B, 0x0D, 0x11);
    public static readonly Brush SolidFallbackBrush = CreateSolid();

    public static bool Apply(Window window)
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

        if (!NativeMethods.IsWindows11())
        {
            window.Background = SolidFallbackBrush;
            return false;
        }

        var margins = NativeMethods.Margins.Full;
        NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins);

        // Prefer mica-like glass: value 3 first (requested), then 4 acrylic/tabbed,
        // then official Mica (2), then the early Windows 11 mica switch.
        if (!TryBackdrop(hwnd, NativeMethods.DWMSBT_TRANSIENTWINDOW) &&
            !TryBackdrop(hwnd, NativeMethods.DWMSBT_TABBEDWINDOW) &&
            !TryBackdrop(hwnd, NativeMethods.DWMSBT_MAINWINDOW))
        {
            var mica = 1;
            NativeMethods.DwmSetWindowAttribute(
                hwnd, NativeMethods.DWMWA_MICA_EFFECT, ref mica, sizeof(int));
        }

        window.Background = System.Windows.Media.Brushes.Transparent;
        WindowCorners.Apply(window);
        return true;
    }

    public static void RemoveBackdrop(Window window, Color? solid = null)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var source = HwndSource.FromHwnd(hwnd);
        var fill = solid ?? SolidFallback;
        if (fill.A == 0)
            fill = SolidFallback;
        else if (fill.A < 255)
            fill = Color.FromRgb(fill.R, fill.G, fill.B);

        if (source?.CompositionTarget is { } target)
            target.BackgroundColor = fill;

        var none = NativeMethods.DWMSBT_NONE;
        NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int));
        var micaOff = 0;
        NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_MICA_EFFECT, ref micaOff, sizeof(int));

        window.Background = new SolidColorBrush(fill);
        WindowCorners.Apply(window);
    }

    public static bool TryApply(Window window) => Apply(window);

    public static bool IsWindows11() => NativeMethods.IsWindows11();

    private static bool TryBackdrop(IntPtr hwnd, int type)
    {
        var value = type;
        return NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref value, sizeof(int)) == 0;
    }

    private static SolidColorBrush CreateSolid()
    {
        var brush = new SolidColorBrush(SolidFallback);
        brush.Freeze();
        return brush;
    }
}
