using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Klip.Native;

namespace Klip.Services;

public static class WindowCorners
{
    public const double Radius = 16;

    public static CornerRadius FrameRadius => new(Radius);

    /// <summary>
    /// True = clip the HWND to a rounded region (opaque theme / wallpaper).
    /// False = let DWM round the glass window (Mica).
    /// </summary>
    public static bool ClipHost { get; set; } = true;

    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        if (window.WindowState == WindowState.Maximized || window.ActualWidth < 8 || window.ActualHeight < 8)
        {
            NativeMethods.SetWindowRgn(hwnd, IntPtr.Zero, true);
            SetDwm(hwnd, NativeMethods.DWMWCP_DONOTROUND);
            return;
        }

        SetDwm(hwnd, NativeMethods.DWMWCP_ROUND);

        if (!ClipHost)
        {
            NativeMethods.SetWindowRgn(hwnd, IntPtr.Zero, true);
            return;
        }

        var (scaleX, scaleY) = GetScale(window);
        var width = Math.Max(1, (int)Math.Round(window.ActualWidth * scaleX));
        var height = Math.Max(1, (int)Math.Round(window.ActualHeight * scaleY));
        var ellipseX = Math.Max(2, (int)Math.Round(Radius * 2 * scaleX));
        var ellipseY = Math.Max(2, (int)Math.Round(Radius * 2 * scaleY));

        var region = NativeMethods.CreateRoundRectRgn(0, 0, width + 1, height + 1, ellipseX, ellipseY);
        if (region == IntPtr.Zero)
            return;

        if (NativeMethods.SetWindowRgn(hwnd, region, true) == 0)
            NativeMethods.DeleteObject(region);
    }

    public static void ClipFrame(System.Windows.Controls.Border frame, Window window)
    {
        frame.CornerRadius = window.WindowState == WindowState.Maximized
            ? new CornerRadius(0)
            : FrameRadius;
        frame.Clip = null;
    }

    private static void SetDwm(IntPtr hwnd, int preference)
    {
        var value = preference;
        NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref value, sizeof(int));
    }

    private static (double X, double Y) GetScale(Visual visual)
    {
        if (PresentationSource.FromVisual(visual) is HwndSource { CompositionTarget: { } target })
            return (target.TransformToDevice.M11, target.TransformToDevice.M22);
        return (1, 1);
    }
}
