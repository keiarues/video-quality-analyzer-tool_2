using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace VideoQualityAnalyzer.App;

public partial class MainWindow
{
    private bool seekSliderPointerActive;
    private bool seekSliderDragging;
    private TimeSpan? pendingSeekPosition;
    private long sliderSeekGeneration;

    private void SeekSlider_MouseDown(object sender, MouseButtonEventArgs e)
    {
        seekSliderPointerActive = true;
        seekTimer.Stop();
        ++sliderSeekGeneration;
    }

    private void SeekSlider_DragStarted(object sender, DragStartedEventArgs e)
    {
        seekSliderDragging = true;
        seekTimer.Stop();
        ++sliderSeekGeneration;
    }

    private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!initialized || programmaticSlider) return;
        pendingSeekPosition = TimeSpan.FromSeconds(e.NewValue);
        ++sliderSeekGeneration;
        seekTimer.Stop();
        if (!seekSliderPointerActive && !seekSliderDragging) seekTimer.Start();
        UpdateTimeDisplay();
    }

    private void SeekSlider_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (seekSliderPointerActive || seekSliderDragging) CommitSeekSlider();
    }

    private void SeekSlider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (seekSliderDragging || seekSliderPointerActive) CommitSeekSlider();
    }

    private async void CommitSeekSlider()
    {
        seekTimer.Stop();
        seekSliderPointerActive = seekSliderDragging = false;
        if (pendingSeekPosition is not { } target) return;
        var generation = ++sliderSeekGeneration;
        try
        {
            // 表示更新後のSlider.Valueを読み直さず、利用者が指定した位置を確定する。
            ApplySeek(target);
            if (comparisonActive) await comparisonChangeTask;
        }
        finally
        {
            if (generation == sliderSeekGeneration)
            {
                pendingSeekPosition = null;
                if (!closing) UpdateTimeDisplay();
            }
        }
    }
}
