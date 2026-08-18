using System.Windows;
using System.Windows.Media;
using Klip.Native;

namespace Klip.Services;

public static class WindowCorners
{
    public const double Radius = 16;

    public static CornerRadius FrameRadius => new(Radius);

    public static void Apply(Window window)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        NativeMethods.SetWindowRgn(hwnd, IntPtr.Zero, true);
        var corner = window.WindowState == WindowState.Maximized
            ? NativeMethods.DWMWCP_DONOTROUND
            : NativeMethods.DWMWCP_DONOTROUND;
        NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
    }

    public static void ClipFrame(System.Windows.Controls.Border frame, Window window)
    {
        if (window.WindowState == WindowState.Maximized)
        {
            frame.CornerRadius = new CornerRadius(0);
            frame.Clip = null;
            return;
        }

        frame.CornerRadius = FrameRadius;
        var w = frame.ActualWidth;
        var h = frame.ActualHeight;
        if (w < 1 || h < 1)
        {
            frame.Clip = null;
            return;
        }

        frame.Clip = new RectangleGeometry(new Rect(0, 0, w, h), Radius, Radius);
    }
}
