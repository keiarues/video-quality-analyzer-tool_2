using System.Windows;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.GpuPreview;

namespace VideoQualityAnalyzer.App;

public partial class MainWindow
{
    private ComparisonPanel? comparisonPanel;
    private bool comparisonActive;
    private Task comparisonChangeTask = Task.CompletedTask;
    private string? comparisonLeftId;
    private string? comparisonRightId;
    private long comparisonGeneration;

    private static bool CanCompare(VideoDescriptor? left, VideoDescriptor? right) => left is not null && right is not null &&
        left.Width == right.Width && left.Height == right.Height && left.AverageFrameRate > 0 &&
        Math.Abs(left.AverageFrameRate - right.AverageFrameRate) < 0.001 &&
        !left.IsVariableFrameRate && !right.IsVariableFrameRate;

    private void UpdateEmbeddedComparison(VideoDescriptor? leftVideo, VideoDescriptor? rightVideo, TimeSpan position, bool resume)
    {
        PreviewZoomControls.Visibility = Visibility.Visible;
        var wasComparisonVisible = ComparisonHost.Visibility == Visibility.Visible;
        InitialPreviewTabs.Visibility = comparisonActive ? Visibility.Collapsed : Visibility.Visible;
        ComparisonHost.Visibility = comparisonActive ? Visibility.Visible : Visibility.Collapsed;
        LegacyPreviewGrid.Visibility = comparisonActive ? Visibility.Collapsed : Visibility.Visible;
        if (!comparisonActive)
        {
            ++comparisonGeneration;
            if (comparisonPanel is not null && wasComparisonVisible) InitialPreviewTabs.SelectedIndex = comparisonPanel.SelectedModeIndex;
            comparisonPanel?.Clear(position);
            comparisonLeftId = comparisonRightId = null;
            basePosition = position;
            if (resume) playbackClock.Restart(); else playbackClock.Reset();
            foreach (var slot in new[] { left, right })
                if (slot.Video is { } video && !slot.Av1.IsOpen) slot.Av1.Open(video, position, resume);
            return;
        }
        left.Av1.Close();
        right.Av1.Close();
        left.LastShownPosition = right.LastShownPosition = null;
        InitializePreviewZoomControls();
        comparisonPanel.SetVideoChoices(videos.Select(entry => entry.Descriptor.Path));
        if (!wasComparisonVisible) comparisonPanel.SelectMode(InitialPreviewTabs.SelectedIndex);
        if (comparisonLeftId == leftVideo!.Id && comparisonRightId == rightVideo!.Id) return;
        comparisonLeftId = leftVideo.Id;
        comparisonRightId = rightVideo!.Id;
        var generation = ++comparisonGeneration;
        comparisonChangeTask = ConfigureComparisonAsync(generation, leftVideo, rightVideo, position);
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(comparisonPanel))]
    private void InitializePreviewZoomControls()
    {
        if (comparisonPanel is null)
        {
            comparisonPanel = new ComparisonPanel(string.Empty, string.Empty, 0,
                videoPaths: videos.Select(entry => entry.Descriptor.Path), showBlur: false, embedded: true);
            comparisonPanel.EnableZoom();
            comparisonPanel.MoveZoomControlsTo(PreviewZoomControls);
            comparisonPanel.SourcesRequested += (leftPath, rightPath) =>
            {
                previewLeftOverride = videos.FirstOrDefault(entry => string.Equals(entry.Descriptor.Path, leftPath, StringComparison.OrdinalIgnoreCase))?.Descriptor.Id;
                previewRightOverride = videos.FirstOrDefault(entry => string.Equals(entry.Descriptor.Path, rightPath, StringComparison.OrdinalIgnoreCase))?.Descriptor.Id;
                UpdateSelection();
            };
            ComparisonHost.Content = comparisonPanel;
            comparisonPanel.FrameShown += RequestPlaybackRefresh;
            comparisonPanel.PlaybackEnded += () =>
            {
                if (!comparisonActive) return;
                basePosition = comparisonPanel.Position;
                isPlaying = false;
                playbackClock.Stop();
                LeftMedia.Pause();
                UpdateTimeDisplay();
            };
        }
    }

    private async Task ConfigureComparisonAsync(long generation, VideoDescriptor leftVideo, VideoDescriptor rightVideo, TimeSpan position)
    {
        try
        {
            await comparisonPanel!.SetSourcesAsync(leftVideo, rightVideo, position, false);
            if (generation != comparisonGeneration || !comparisonActive || closing) return;
            left.Ready = right.Ready = comparisonPanel.IsReady;
            if (isPlaying && comparisonPanel.IsReady) comparisonPanel.Play();
            UpdateTimeDisplay();
        }
        catch (Exception exception) { if (!closing) StatusText.Text = $"比較映像を準備できません: {exception.GetType().Name}"; }
    }
}
