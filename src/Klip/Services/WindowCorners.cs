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

        if (window.WindowState == WindowState.Maximized)
        {
            NativeMethods.SetWindowRgn(hwnd, IntPtr.Zero, true);
            SetDwm(hwnd, NativeMethods.DWMWCP_DONOTROUND);
            return;
        }

        var (scaleX, scaleY) = GetScale(window);
        var width = Math.Max(1, (int)Math.Round(window.ActualWidth * scaleX));
        var height = Math.Max(1, (int)Math.Round(window.ActualHeight * scaleY));
        if (width < 8 || height < 8)
            return;

        var ellipseX = Math.Max(2, (int)Math.Round(Radius * 2 * scaleX));
        var ellipseY = Math.Max(2, (int)Math.Round(Radius * 2 * scaleY));
        var region = NativeMethods.CreateRoundRectRgn(0, 0, width + 1, height + 1, ellipseX, ellipseY);
        if (region == IntPtr.Zero)
            return;

        if (NativeMethods.SetWindowRgn(hwnd, region, true) == 0)
            NativeMethods.DeleteObject(region);

        SetDwm(hwnd, NativeMethods.DWMWCP_ROUND);
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
            return;
        frame.Clip = new RectangleGeometry(new Rect(0, 0, w, h), Radius, Radius);
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
