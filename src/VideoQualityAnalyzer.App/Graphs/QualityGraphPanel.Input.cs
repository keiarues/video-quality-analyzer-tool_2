using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VideoQualityAnalyzer.App;

public sealed partial class QualityGraphPanel
{
    public event EventHandler<MouseWheelEventArgs>? TimelineWheel;

    private void InitializePlayheadInput()
    {
        timeline.Focusable = true;
        timeline.PreviewMouseLeftButtonDown += (_, _) => timeline.Focus();
        timeline.PreviewMouseWheel += (_, e) => TimelineWheel?.Invoke(this, e);
        timeline.Cursor = System.Windows.Input.Cursors.SizeWE;
        timeline.MouseLeftButtonDown += (_, e) =>
        {
            var area = PlotArea(timeline);
            var position = e.GetPosition(timeline);
            if (maximumTime <= 0 || !area.Contains(position) || !timeline.CaptureMouse()) return;
            BeginPlayheadDrag((position.X - area.Left) / area.Width);
            e.Handled = true;
        };
        timeline.MouseMove += (_, e) =>
        {
            if (!playheadDragging) return;
            var area = PlotArea(timeline);
            MovePlayheadDrag((e.GetPosition(timeline).X - area.Left) / area.Width);
            e.Handled = true;
        };
        timeline.MouseLeftButtonUp += (_, e) =>
        {
            if (!playheadDragging) return;
            var area = PlotArea(timeline);
            MovePlayheadDrag((e.GetPosition(timeline).X - area.Left) / area.Width);
            EndPlayheadDrag();
            timeline.ReleaseMouseCapture();
            e.Handled = true;
        };
        timeline.LostMouseCapture += (_, _) => { if (playheadDragging) EndPlayheadDrag(); };
    }
}
