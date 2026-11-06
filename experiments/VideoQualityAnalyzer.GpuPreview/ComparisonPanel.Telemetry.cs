using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace VideoQualityAnalyzer.GpuPreview;

public partial class ComparisonPanel
{
    private long observationStamp;
    private long? observedLeft;
    private long? observedRight;
    private int observedLeftCount;
    private int observedRightCount;
    public double? ObservedLeftFps { get; private set; }
    public double? ObservedRightFps { get; private set; }
    // 現在の共通CFR経路では、番号から求める相対PTS。復号器の実PTSではない。
    public double? RelativePtsDifferenceMilliseconds => currentLeft is not null && currentRight is not null && sourceFrameRate > 0
        ? (currentLeft.FrameNumber - currentRight.FrameNumber) * 1000.0 / sourceFrameRate : null;

    private void ObservePlayback(object? sender, EventArgs args)
    {
        if (!IsVisible || Window.GetWindow(this)?.WindowState == WindowState.Minimized ||
            playbackCancellation is null || playbackPaused)
        {
            observationStamp = 0;
            observedLeft = observedRight = null;
            observedLeftCount = observedRightCount = 0;
            ObservedLeftFps = ObservedRightFps = null;
            return;
        }
        var now = Stopwatch.GetTimestamp();
        if (observationStamp == 0) observationStamp = now;
        if (currentLeft is not null && observedLeft != currentLeft.FrameNumber)
        {
            observedLeft = currentLeft.FrameNumber;
            observedLeftCount++;
        }
        if (currentRight is not null && observedRight != currentRight.FrameNumber)
        {
            observedRight = currentRight.FrameNumber;
            observedRightCount++;
        }
        var seconds = Stopwatch.GetElapsedTime(observationStamp, now).TotalSeconds;
        if (seconds < 1) return;
        ObservedLeftFps = observedLeftCount / seconds;
        ObservedRightFps = observedRightCount / seconds;
        observedLeftCount = observedRightCount = 0;
        observationStamp = now;
    }
}