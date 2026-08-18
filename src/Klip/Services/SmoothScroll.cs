using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace Klip.Services;

public static class SmoothScroll
{
    private static readonly DependencyProperty OffsetProperty = DependencyProperty.RegisterAttached(
        "Offset",
        typeof(double),
        typeof(SmoothScroll),
        new PropertyMetadata(0d, OnOffset));

    private static readonly DependencyProperty TargetProperty = DependencyProperty.RegisterAttached(
        "Target",
        typeof(double),
        typeof(SmoothScroll),
        new PropertyMetadata(double.NaN));

    private static readonly DependencyProperty HookedProperty = DependencyProperty.RegisterAttached(
        "Hooked",
        typeof(bool),
        typeof(SmoothScroll),
        new PropertyMetadata(false));

    public static void Attach(ScrollViewer viewer)
    {
        if (viewer is null || (bool)viewer.GetValue(HookedProperty))
            return;
        viewer.SetValue(HookedProperty, true);
        viewer.PreviewMouseWheel += OnWheel;
        viewer.ScrollChanged += OnScrollChanged;
    }

    private static void OnOffset(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer viewer)
            viewer.ScrollToVerticalOffset((double)e.NewValue);
    }

    private static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer viewer || viewer.ScrollableHeight <= 0)
            return;

        e.Handled = true;
        var current = viewer.VerticalOffset;
        var stored = (double)viewer.GetValue(TargetProperty);
        var from = double.IsNaN(stored) ? current : stored;
        var step = e.Delta * 0.72;
        var target = Math.Clamp(from - step, 0, viewer.ScrollableHeight);
        viewer.SetValue(TargetProperty, target);

        var anim = new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(340))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        };
        anim.Completed += (_, _) =>
        {
            viewer.ScrollToVerticalOffset(target);
            viewer.BeginAnimation(OffsetProperty, null);
            if (Math.Abs((double)viewer.GetValue(TargetProperty) - target) < 0.5)
                viewer.SetValue(TargetProperty, double.NaN);
        };
        viewer.BeginAnimation(OffsetProperty, anim);
    }

    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer viewer || e.ExtentHeightChange == 0)
            return;
        var stored = (double)viewer.GetValue(TargetProperty);
        if (double.IsNaN(stored))
            return;
        viewer.SetValue(TargetProperty, Math.Clamp(stored, 0, viewer.ScrollableHeight));
    }
}
