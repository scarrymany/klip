using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Klip.Native;

namespace Klip.Services;

public static class WindowCorners
{
    public const double Radius = 16;

    public static CornerRadius FrameRadius => new(Radius);

    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        // Never use SetWindowRgn: with a transparent WPF surface it composites
        // against the default white window color and draws a bright halo.
        NativeMethods.SetWindowRgn(hwnd, IntPtr.Zero, true);

        SetDwm(hwnd, window.WindowState == WindowState.Maximized
            ? NativeMethods.DWMWCP_DONOTROUND
            : NativeMethods.DWMWCP_ROUND);
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
        frame.Clip = null;
    }

    private static void SetDwm(IntPtr hwnd, int preference)
    {
        var value = preference;
        NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref value, sizeof(int));
    }
}
