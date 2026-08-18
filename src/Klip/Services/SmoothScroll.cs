using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Klip.Services;

public static class SmoothScroll
{
    private const double WheelScale = 0.52;
    private const double Lerp = 0.2;
    private const double Snap = 0.35;

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State",
        typeof(Engine),
        typeof(SmoothScroll));

    public static void Attach(ScrollViewer viewer)
    {
        if (viewer is null || viewer.GetValue(StateProperty) is Engine)
            return;

        var engine = new Engine(viewer);
        viewer.SetValue(StateProperty, engine);
        viewer.PreviewMouseWheel += engine.OnWheel;
        viewer.Unloaded += engine.OnUnloaded;
    }

    private sealed class Engine
    {
        private readonly ScrollViewer _viewer;
        private double _target = double.NaN;
        private bool _running;

        public Engine(ScrollViewer viewer) => _viewer = viewer;

        public void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if (_viewer.ScrollableHeight <= 0)
                return;

            e.Handled = true;
            if (double.IsNaN(_target))
                _target = _viewer.VerticalOffset;
            _target = Math.Clamp(_target - e.Delta * WheelScale, 0, _viewer.ScrollableHeight);
            if (!_running)
            {
                _running = true;
                CompositionTarget.Rendering += OnFrame;
            }
        }

        public void OnUnloaded(object sender, RoutedEventArgs e) => Stop();

        private void OnFrame(object? sender, EventArgs e)
        {
            if (!_viewer.IsLoaded || double.IsNaN(_target))
            {
                Stop();
                return;
            }

            var dest = Math.Clamp(_target, 0, _viewer.ScrollableHeight);
            var cur = _viewer.VerticalOffset;
            var next = cur + (dest - cur) * Lerp;
            if (Math.Abs(dest - next) < Snap)
            {
                _viewer.ScrollToVerticalOffset(dest);
                _target = double.NaN;
                Stop();
                return;
            }

            _viewer.ScrollToVerticalOffset(next);
        }

        private void Stop()
        {
            if (!_running)
                return;
            _running = false;
            CompositionTarget.Rendering -= OnFrame;
        }
    }
}
