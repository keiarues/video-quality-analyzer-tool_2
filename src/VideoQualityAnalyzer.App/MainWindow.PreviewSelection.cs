using System.Windows.Controls;
using VideoQualityAnalyzer.Core;

namespace VideoQualityAnalyzer.App;

public partial class MainWindow
{
    private void VideoList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (initialized && !suppressSelection)
        {
            previewLeftOverride = previewRightOverride = null;
            if (VideoList.SelectedItems.Count > 0) ReferenceList.UnselectAll();
            UpdateSelection();
        }
    }

    private string? previewLeftOverride;
    private string? previewRightOverride;
    private bool syncingPreviewSelectors;

    private void PreviewVideo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (syncingPreviewSelectors || !initialized || sender is not ComboBox { SelectedItem: VideoEntry entry } combo) return;
        if (combo == PreviewLeftSelect) previewLeftOverride = entry.Descriptor.Id;
        else previewRightOverride = entry.Descriptor.Id;
        UpdateSelection();
    }

    private void SyncPreviewSelectors()
    {
        syncingPreviewSelectors = true;
        try
        {
            PreviewLeftSelect.ItemsSource = PreviewRightSelect.ItemsSource = videos;
            PreviewLeftSelect.SelectedItem = videos.FirstOrDefault(entry => entry.Descriptor.Id == left.Video?.Id);
            PreviewRightSelect.SelectedItem = videos.FirstOrDefault(entry => entry.Descriptor.Id == right.Video?.Id);
        }
        finally { syncingPreviewSelectors = false; }
    }

    private void UpdateSelection()
    {
        RefreshLibraryLists();
        var position = CurrentPosition();
        var resume = isPlaying;
        var ordered = videos.Select(item => item.Descriptor).ToArray();
        foreach (var item in videos)
            item.IsReference = item.Descriptor.Id == referenceId;
        var selected = VideoList.SelectedItems.Cast<VideoEntry>().Select(item => item.Descriptor.Id).ToArray();
        var choice = SelectionService.ApplyOverrides(
            SelectionService.Resolve(ordered, referenceId, selected, previousRightId, ++selectionGeneration),
            ordered, previewLeftOverride, previewRightOverride);
        log.Record("selection_requested", "both", choice.LeftVideoId, "requested",
            detail: $"right_video_id={choice.RightVideoId ?? "none"};generation={choice.Generation}");
        previousRightId = choice.RightVideoId;
        frameTimesCancellation.Cancel();
        frameTimesCancellation.Dispose();
        frameTimesCancellation = new CancellationTokenSource();
        var leftVideo = ordered.FirstOrDefault(video => video.Id == choice.LeftVideoId);
        var rightVideo = ordered.FirstOrDefault(video => video.Id == choice.RightVideoId);
        comparisonActive = CanCompare(leftVideo, rightVideo);
        SetSlot(left, leftVideo);
        SetSlot(right, rightVideo);
        RefreshEvaluatedPlayback(seekOnChange: false);
        resume = isPlaying;
        UpdateEmbeddedComparison(leftVideo, rightVideo, position, resume);
        RightMedia.IsMuted = true;
        UpdateInfo();
        SyncPreviewSelectors();
        RefreshEmbeddedGraphs();
        UpdateTimeDisplay();
    }

}
