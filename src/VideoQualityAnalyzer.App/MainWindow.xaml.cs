using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Diagnostics;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<VideoEntry> videos = [];
    private bool playbackRefreshQueued;
    private readonly DispatcherTimer displayTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer seekTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly Stopwatch playbackClock = new();
    private readonly MeasurementLog log = new();
    private readonly FfprobeService? probe;
    private readonly SlotState left;
    private readonly SlotState right;
    private CancellationTokenSource frameTimesCancellation = new();
    private VideoSortOrder activeSort;
    private bool sortDescending;
    private string? referenceId;
    private readonly Dictionary<(string VideoId, QualityMetric Metric), QualityResult> qualityResults = [];
    private string? previousRightId;
    private long selectionGeneration;
    private TimeSpan basePosition;
    private DateTimeOffset? driftStart;
    private long previousSampleStamp;
    private TimeSpan previousCpuTime;
    private double? cpuPercent;
    private double memoryMb;
    private bool isPlaying;
    private bool programmaticSlider;
    private bool suppressSelection;
    private bool initialized;
    private bool closing;

    public MainWindow()
    {
        InitializeComponent();
        App.RecordStartup("メイン画面XAMLの読み込み完了");
        left = new SlotState("left", LeftMedia, LeftAv1Image, LeftName);
        right = new SlotState("right", RightMedia, RightAv1Image, RightName);
        foreach (var slot in new[] { left, right })
        {
            slot.Av1.FrameShown = position =>
            {
                if (slot.Video is null) return;
                slot.LastShownPosition = position;
                RequestPlaybackRefresh();
                if (slot.Ready) return;
                slot.Ready = true;
                log.Record("preview_frame_shown", slot.Side, slot.Video.Id, "ffmpeg_wpf_bitmap_copy",
                    detail: $"estimated_pts_ms={position.TotalMilliseconds:F0};gpu_presentation=unmeasured");
            };
            slot.Av1.Failed = exception =>
            {
                slot.Ready = false;
                log.Record("preview_failed", slot.Side, slot.Video?.Id, "failed",
                    detail: $"{exception.GetType().Name};hresult=0x{exception.HResult:X8}");
            };
        }
        VideoList.ItemsSource = new System.Windows.Data.ListCollectionView(videos)
        { Filter = item => item is VideoEntry entry && entry.Descriptor.Id != referenceId };
        App.RecordStartup("FFmpeg・FFprobeの探索開始");
        var path = FfprobeService.FindExecutable();
        App.RecordStartup("FFmpeg・FFprobeの探索完了");
        if (path is not null)
        {
            probe = new FfprobeService(path);
            SetWindowStatus("動画を追加してください。FFprobeを検出しました。");
        }
        else
        {
            SetWindowStatus("FFmpegまたはFFprobeが見つかりません。オプション → 設定でフォルダーを指定し、再起動してください。");
        }

        displayTimer.Tick += DisplayTimer_Tick;
        displayTimer.Start();
        seekTimer.Tick += (_, _) =>
        {
            seekTimer.Stop();
            if (!seekSliderPointerActive && !seekSliderDragging) CommitSeekSlider();
        };
        SeekSlider.AddHandler(System.Windows.Controls.Primitives.Thumb.DragStartedEvent,
            new System.Windows.Controls.Primitives.DragStartedEventHandler(SeekSlider_DragStarted), true);
        SeekSlider.AddHandler(System.Windows.Controls.Primitives.Thumb.DragCompletedEvent,
            new System.Windows.Controls.Primitives.DragCompletedEventHandler(SeekSlider_DragCompleted), true);
        LeftMedia.Volume = VolumeSlider.Value;
        RightMedia.IsMuted = true;
        initialized = true;
        InitializeEmbeddedGraphs();
        InitializeEvaluationControls();
        // FFmpeg未配置でも本体と設定画面を開けるよう、比較画面の初期化を保留する。
        if (probe is not null) InitializePreviewZoomControls();
        log.Record("session_start", "none", null, "ready", detail: "presented_fps=unmeasured;decode_mode=unknown");
        Loaded += async (_, _) =>
        {
            if (Environment.GetCommandLineArgs().Contains("--verify-initial-preview"))
            {
                try
                {
                    UpdateLayout();
                    if (videos.Count != 0 || !InitialPreviewTabs.IsVisible || InitialPreviewTabs.Items.Count != 3 || InitialPreviewTabs.SelectedIndex != 0)
                        throw new InvalidOperationException("動画未登録時にプレビュータブが表示されません。");
                    foreach (var index in new[] { 1, 2, 0 })
                    {
                        InitialPreviewTabs.SelectedIndex = index;
                        UpdateLayout();
                        if (!InitialPreviewTabs.IsVisible) throw new InvalidOperationException("初期タブを切り替えられません。");
                    }
                    Console.WriteLine("初期プレビュー: 未登録時の3タブ表示と切り替えが合格");
                    Application.Current.Shutdown(0);
                }
                catch (Exception exception) { Console.Error.WriteLine(exception.Message); Application.Current.Shutdown(1); }
                return;
            }
            if (Environment.GetCommandLineArgs().Contains("--verify-sidebar"))
            {
                try
                {
                    SidebarColumn.Width = new GridLength(580);
                    UpdateLayout();
                    var width = SidebarColumn.ActualWidth;
                    SidebarToggle_Click(this, new RoutedEventArgs());
                    UpdateLayout();
                    if (SidebarPanel.Visibility != Visibility.Collapsed || SidebarColumn.ActualWidth != 0)
                        throw new InvalidOperationException("一覧の最小化に失敗しました。");
                    SidebarToggle_Click(this, new RoutedEventArgs());
                    UpdateLayout();
                    if (SidebarPanel.Visibility != Visibility.Visible || Math.Abs(SidebarColumn.ActualWidth - width) > 1)
                        throw new InvalidOperationException("一覧の幅の復元に失敗しました。");
                    Application.Current.Shutdown(0);
                }
                catch { Application.Current.Shutdown(1); }
                return;
            }
            var startupFiles = Environment.GetCommandLineArgs().Skip(1).Where(File.Exists).ToArray();
            if (startupFiles.Length > 0)
                await AddFilesAsync(startupFiles);
            var graphPlaybackCheck = Environment.GetCommandLineArgs().FirstOrDefault(arg => arg.StartsWith("--verify-main-graph-playback="));
            if (graphPlaybackCheck is not null) await VerifyMainGraphPlaybackAsync(graphPlaybackCheck.Split('=', 2)[1]);
            else if (Environment.GetCommandLineArgs().Contains("--verify-main-features")) VerifyMainFeatures();
            else if (Environment.GetCommandLineArgs().Contains("--verify-main-comparison")) await VerifyEmbeddedComparisonAsync();
            else if (Environment.GetCommandLineArgs().Contains("--verify-main-graphs")) await VerifyEmbeddedGraphsAsync();
            else if (Environment.GetCommandLineArgs().Contains("--verify-preview-controls")) await VerifyPreviewControlsAsync();
            else if (Environment.GetCommandLineArgs().Contains("--verify-main-refresh")) await VerifyPlaybackRefreshAsync();
            else if (Environment.GetCommandLineArgs().Contains("--verify-main-seek")) await VerifySeekSliderAsync();
            else if (Environment.GetCommandLineArgs().Contains("--verify-main-evaluation")) await VerifyMainEvaluationAsync();
        };
    }

    private void EvaluationHelpBorder_Loaded(object sender, RoutedEventArgs e)
    {
        if (double.IsNaN(EvaluationHelpBorder.Height))
            EvaluationHelpBorder.Height = EvaluationHelpBorder.ActualHeight * 2;
    }

    private double expandedSidebarWidth = 700;
    private bool sidebarCollapsed;

    private void SidebarToggle_Click(object sender, RoutedEventArgs e)
    {
        if (!sidebarCollapsed)
        {
            expandedSidebarWidth = SidebarColumn.ActualWidth;
            SidebarPanel.Visibility = SidebarSplitter.Visibility = Visibility.Collapsed;
            SidebarColumn.MinWidth = 0;
            SidebarColumn.Width = new GridLength(0);
            SidebarSplitterColumn.Width = new GridLength(20);
        }
        else
        {
            SidebarColumn.MinWidth = 300;
            SidebarColumn.Width = new GridLength(Math.Max(300, expandedSidebarWidth));
            SidebarSplitterColumn.Width = new GridLength(20);
            SidebarPanel.Visibility = SidebarSplitter.Visibility = Visibility.Visible;
        }
        sidebarCollapsed = !sidebarCollapsed;
        SidebarToggleButton.Content = sidebarCollapsed ? "▶" : "◀";
        SidebarToggleButton.ToolTip = sidebarCollapsed ? "一覧を復元" : "一覧を最小化";
        System.Windows.Automation.AutomationProperties.SetName(SidebarToggleButton, (string)SidebarToggleButton.ToolTip);
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "動画を追加",
            Filter = "動画ファイル|*.mkv;*.mp4;*.mov;*.avi;*.webm;*.m4v|すべてのファイル|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) == true)
            await AddFilesAsync(dialog.FileNames);
    }

    private async Task AddFilesAsync(IEnumerable<string> paths, string[]? droppedFolders = null)
    {
        if (probe is null)
        {
            SetWindowStatus("動画を読み込むにはFFmpegとFFprobeが必要です。オプション → 設定でフォルダーを指定し、再起動してください。");
            return;
        }
        if (evaluationCancellation is not null)
        {
            SetWindowStatus("評価中は動画を追加できません。", evaluation: true);
            return;
        }

        pendingVideoLoads++;
        try
        {
        foreach (var input in paths)
        {
            var path = System.IO.Path.GetFullPath(input);
            if (videos.Any(item => string.Equals(item.Descriptor.Path, path, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (!File.Exists(path))
            {
                SetWindowStatus("ファイルが見つかりません: " + System.IO.Path.GetFileName(path));
                continue;
            }

            SetWindowStatus("動画情報を取得中: " + System.IO.Path.GetFileName(path));
            try
            {
                var descriptor = await probe.ProbeAsync(path, CancellationToken.None);
                var entry = new VideoEntry(descriptor);
                videos.Add(entry);
                referenceId ??= descriptor.Id;
                entry.IsReference = referenceId == descriptor.Id;
                if (!entry.IsReference && droppedFolders is not null)
                    entry.Group = VideoDropFiles.GetGroup(path, droppedFolders);
                VideoList.Items.Refresh();
                log.Record("file_added", "none", descriptor.Id, "ok",
                    detail: $"codec={descriptor.Codec};resolution={descriptor.Width}x{descriptor.Height};fps={descriptor.AverageFrameRate:F3}");
            }
            catch (Exception exception)
            {
                SetWindowStatus($"動画情報を取得できません: {System.IO.Path.GetFileName(path)} ({exception.GetType().Name})");
                log.Record("probe_failed", "none", null, "failed", detail: exception.GetType().Name);
            }
        }

        UpdateSelection();
        if (activeSort != VideoSortOrder.Registration) RefreshVideoOrder();
        SetWindowStatus($"{videos.Count}本を登録しました。");
        }
        finally { pendingVideoLoads--; }
    }

    private void SetReference_Click(object sender, RoutedEventArgs e)
    {
        if (evaluationCancellation is not null) return;
        if (VideoList.SelectedItem is not VideoEntry entry)
            return;
        if (referenceId != entry.Descriptor.Id) qualityResults.Clear();
        referenceId = entry.Descriptor.Id;
        foreach (var item in videos)
            item.IsReference = item.Descriptor.Id == referenceId;
        RefreshQualityValues();
        suppressSelection = true;
        VideoList.Items.Refresh();
        suppressSelection = false;
        UpdateSelection();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (evaluationCancellation is not null) return;
        var removed = VideoList.SelectedItems.Cast<VideoEntry>().Concat(ReferenceList.SelectedItems.Cast<VideoEntry>()).Distinct().ToArray();
        RemoveVideos(removed);
    }

    private void RemoveAll_Click(object sender, RoutedEventArgs e)
    {
        if (evaluationCancellation is not null) return;
        RemoveVideos(videos.ToArray());
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
    private void Settings_Click(object sender, RoutedEventArgs e) => new SettingsWindow { Owner = this }.ShowDialog();
    private void About_Click(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();

    private void RemoveVideos(VideoEntry[] removed)
    {
        suppressSelection = true;
        foreach (var item in removed)
        {
            videos.Remove(item);
            log.Record("file_removed", "none", item.Descriptor.Id, "ok");
        }
        if (!videos.Any(item => item.Descriptor.Id == referenceId))
        {
            qualityResults.Clear();
            referenceId = videos.FirstOrDefault()?.Descriptor.Id;
        }
        foreach (var key in qualityResults.Keys.Where(key => removed.Any(item => item.Descriptor.Id == key.VideoId)).ToArray())
            qualityResults.Remove(key);
        foreach (var item in videos)
            item.IsReference = item.Descriptor.Id == referenceId;
        RefreshQualityValues();
        VideoList.Items.Refresh();
        suppressSelection = false;
        UpdateSelection();
    }

    private void VideoList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 行・列見出し・スクロール操作は通常どおり扱い、一覧の空白だけで解除する。
        var element = e.OriginalSource as DependencyObject;
        while (element is not null && element != VideoList)
        {
            if (element is DataGridRow or System.Windows.Controls.Primitives.DataGridColumnHeader
                or System.Windows.Controls.Primitives.ScrollBar)
                return;
            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }
        if (element == VideoList) VideoList.UnselectAll();
    }

    private IEnumerable<VideoEntry> SelectedOrAllVideoEntries() =>
        VideoList.SelectedItems.Count == 0 ? videos : VideoList.SelectedItems.Cast<VideoEntry>();

    private void ReferenceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressSelection || ReferenceList.SelectedItem is null) return;
        VideoList.UnselectAll();
    }

    private string? displayedReferenceId;

    private void RefreshLibraryLists()
    {
        var wasSuppressed = suppressSelection;
        suppressSelection = true;
        var selected = VideoList.SelectedItems.Cast<VideoEntry>().Where(entry => entry.Descriptor.Id != referenceId).ToArray();
        var reference = videos.FirstOrDefault(entry => entry.Descriptor.Id == referenceId);
        if (ReferenceList.Items.Count != (reference is null ? 0 : 1) || ReferenceList.Items.Cast<VideoEntry>().FirstOrDefault() != reference)
            ReferenceList.ItemsSource = reference is null ? Array.Empty<VideoEntry>() : new[] { reference };
        if (displayedReferenceId != referenceId)
            VideoList.ItemsSource = new System.Windows.Data.ListCollectionView(videos)
            { Filter = item => item is VideoEntry entry && entry.Descriptor.Id != referenceId };
        displayedReferenceId = referenceId;
        foreach (var entry in selected.Where(videos.Contains))
            if (!VideoList.SelectedItems.Contains(entry)) VideoList.SelectedItems.Add(entry);
        suppressSelection = wasSuppressed;
    }

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
        var choice = SelectionService.Resolve(ordered, referenceId, selected, previousRightId, ++selectionGeneration);
        if (videos.Any(entry => entry.Descriptor.Id == previewLeftOverride)) choice = choice with { LeftVideoId = previewLeftOverride };
        if (videos.Any(entry => entry.Descriptor.Id == previewRightOverride)) choice = choice with { RightVideoId = previewRightOverride };
        log.Record("selection_requested", "both", choice.LeftVideoId, "requested",
            detail: $"right_video_id={choice.RightVideoId ?? "none"};generation={choice.Generation}");
        previousRightId = choice.RightVideoId;
        frameTimesCancellation.Cancel();
        frameTimesCancellation.Dispose();
        frameTimesCancellation = new CancellationTokenSource();
        var leftVideo = ordered.FirstOrDefault(video => video.Id == choice.LeftVideoId);
        var rightVideo = ordered.FirstOrDefault(video => video.Id == choice.RightVideoId);
        comparisonActive = CanCompare(leftVideo, rightVideo);
        SetSlot(left, leftVideo, choice.Generation);
        SetSlot(right, rightVideo, choice.Generation);
        UpdateEmbeddedComparison(leftVideo, rightVideo, position, resume);
        RightMedia.IsMuted = true;
        UpdateInfo();
        SyncPreviewSelectors();
        RefreshEmbeddedGraphs();
        UpdateTimeDisplay();
    }

    private void SetSlot(SlotState slot, VideoDescriptor? video, long generation)
    {
        if (slot.Video?.Id == video?.Id)
            return;

        slot.Av1.Close();
        slot.Media.Close();
        slot.Media.Source = null;
        slot.Video = video;
        slot.LastShownPosition = null;
        slot.Generation = generation;
        slot.Ready = false;
        slot.FpsSampleStamp = 0;
        slot.PlaybackFps = null;
        slot.OpenStarted = Stopwatch.GetTimestamp();
        slot.Name.Text = video is null ? (slot.Side == "left" ? "左: 未選択" : "右: 未選択") :
            (slot.Side == "left" ? "左: " : "右: ") + video.Name;
        if (video is null)
            return;
        if (!File.Exists(video.Path))
        {
            log.Record("open_failed", slot.Side, video.Id, "missing");
            return;
        }

        log.Record("open_requested", slot.Side, video.Id, "started");
        if (!comparisonActive) slot.Av1.Open(video, CurrentPosition(), isPlaying);
        // 左音声は既存のMediaElementで再生する。映像は画像側だけを表示する。
        if (slot.Side == "left" && video.HasAudio && video.Codec != "av1")
        {
            slot.Media.Source = new Uri(video.Path, UriKind.Absolute);
            slot.Media.Play();
        }
    }

    private void LeftMedia_Opened(object sender, RoutedEventArgs e) => MediaOpened(left);
    private void RightMedia_Opened(object sender, RoutedEventArgs e) => MediaOpened(right);

    private void MediaOpened(SlotState slot)
    {
        if (slot.Video is not { } video || slot.Media.Source is null ||
            !string.Equals(slot.Media.Source.LocalPath, video.Path, StringComparison.OrdinalIgnoreCase))
            return;

        // 映像の準備完了はFFmpeg側の最初のフレーム提示で判定する。
        slot.FpsSampleStamp = 0;
        slot.PlaybackFps = null;
        slot.Media.Position = ClampToVideo(CurrentPosition(), video);
        if (isPlaying)
            slot.Media.Play();
        else
            slot.Media.Pause();
        if (slot.Side == "right")
            slot.Media.IsMuted = true;
        var elapsed = Stopwatch.GetElapsedTime(slot.OpenStarted).TotalMilliseconds;
        log.Record("media_opened", slot.Side, video.Id, "ready_not_presented", elapsed,
            detail: "decode_mode=unknown;first_presented_at=unmeasured");
    }

    private void LeftMedia_Failed(object sender, ExceptionRoutedEventArgs e) => MediaFailed(left, e);
    private void RightMedia_Failed(object sender, ExceptionRoutedEventArgs e) => MediaFailed(right, e);

    private void MediaFailed(SlotState slot, ExceptionRoutedEventArgs e)
    {
        if (slot.Av1.IsOpen) return;
        slot.Ready = false;
        slot.PlaybackFps = null;
        log.Record("media_failed", slot.Side, slot.Video?.Id, "failed",
            detail: e.ErrorException?.GetType().Name ?? "unknown");
    }

    private void Media_Ended(object sender, RoutedEventArgs e)
    {
        log.Record("media_ended", ReferenceEquals(sender, LeftMedia) ? "left" : "right", null, "ended");
        StopPlayback();
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (isPlaying) PausePlayback();
        else Play_Click(sender, e);
    }

    private void UpdatePlayPauseButton()
    {
        if (!initialized) return;
        PlayPauseButton.Content = isPlaying ? "⏸" : "▶";
        PlayPauseButton.ToolTip = isPlaying ? "一時停止" : "再生";
        System.Windows.Automation.AutomationProperties.SetName(PlayPauseButton, isPlaying ? "一時停止" : "再生");
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (left.Video is null)
            return;
        if (isPlaying)
            return;
        if (comparisonActive && comparisonPanel is not null)
        {
            isPlaying = true;
            UpdatePlayPauseButton();
            comparisonPanel.Play();
            if (left.Media.Source is not null) { left.Media.Position = CurrentPosition(); left.Media.Play(); }
            return;
        }
        basePosition = ClampToCommon(basePosition);
        if (right.Video is not null)
        {
            right.Av1.Play(basePosition);
        }
        if (left.Video is not null)
        {
            left.Av1.Play(basePosition);
            if (left.Media.Source is not null)
            {
                left.Media.Position = ClampToVideo(basePosition, left.Video);
                left.Media.Play();
            }
        }
        playbackClock.Restart();
        isPlaying = true;
        UpdatePlayPauseButton();
        ResetFpsSamples();
        log.Record("play", "both", left.Video?.Id, "requested", detail: "presented_fps=unmeasured");
    }

    private void Pause_Click(object sender, RoutedEventArgs e) => PausePlayback();

    private void PausePlayback()
    {
        if (!isPlaying)
            return;
        basePosition = ClampToCommon(CurrentPosition());
        comparisonPanel?.Pause();
        playbackClock.Stop();
        isPlaying = false;
        UpdatePlayPauseButton();
        ResetFpsSamples();
        left.Media.Pause();
        right.Media.Pause();
        left.Av1.Pause();
        right.Av1.Pause();
        log.Record("pause", "both", left.Video?.Id, "requested");
        UpdateTimeDisplay();
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => StopPlayback();

    private void StopPlayback()
    {
        playbackClock.Reset();
        isPlaying = false;
        UpdatePlayPauseButton();
        ResetFpsSamples();
        left.LastShownPosition = right.LastShownPosition = null;
        basePosition = TimeSpan.Zero;
        if (comparisonActive && comparisonPanel is not null) comparisonChangeTask = comparisonPanel.SeekAsync(TimeSpan.Zero, false);
        if (left.Av1.IsOpen) left.Av1.Seek(TimeSpan.Zero);
        if (left.Media.Source is not null) left.Media.Position = TimeSpan.Zero;
        if (right.Av1.IsOpen) right.Av1.Seek(TimeSpan.Zero);
        left.Media.Pause();
        right.Media.Pause();
        left.Av1.Pause();
        right.Av1.Pause();
        log.Record("stop", "both", left.Video?.Id, "requested");
        UpdateTimeDisplay();
    }

    private void BackFive_Click(object sender, RoutedEventArgs e) => ApplySeek(CurrentPosition() - TimeSpan.FromSeconds(5));
    private void BackOne_Click(object sender, RoutedEventArgs e) => ApplySeek(CurrentPosition() - TimeSpan.FromSeconds(1));
    private void SetGroup_Click(object sender, RoutedEventArgs e)
    {
        var selected = VideoList.SelectedItems.Cast<VideoEntry>().Where(entry => !entry.IsReference).ToArray();
        if (selected.Length == 0) { SetWindowStatus("グループを設定する比較動画を選択してください。"); return; }
        if (new GroupWindow(selected) { Owner = this }.ShowDialog() == true) RefreshVideoOrder();
    }

    private void ClearGroup_Click(object sender, RoutedEventArgs e)
    {
        var selected = VideoList.SelectedItems.Cast<VideoEntry>().Where(entry => !entry.IsReference).ToArray();
        if (selected.Length == 0) { SetWindowStatus("グループを解除する比較動画を選択してください。"); return; }
        VideoLibrary.SetGroup(selected, "");
        RefreshVideoOrder();
        SetWindowStatus($"{selected.Length}本のグループ設定を解除しました。");
    }

    private void VideoList_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        if (!Enum.TryParse<VideoSortOrder>(e.Column.SortMemberPath, out var order)) return;
        sortDescending = e.Column.SortDirection == System.ComponentModel.ListSortDirection.Ascending;
        activeSort = order;
        foreach (var column in VideoList.Columns) column.SortDirection = null;
        e.Column.SortDirection = sortDescending ? System.ComponentModel.ListSortDirection.Descending
            : System.ComponentModel.ListSortDirection.Ascending;
        RefreshVideoOrder();
    }

    private void RefreshQualityValues()
    {
        foreach (var entry in videos)
        {
            entry.VmafAverage = qualityResults.TryGetValue((entry.Descriptor.Id, QualityMetric.Vmaf), out var vmaf) ? vmaf.Average : null;
            entry.SsimAverage = qualityResults.TryGetValue((entry.Descriptor.Id, QualityMetric.Ssim), out var ssim) ? ssim.Average : null;
            entry.PsnrAverage = qualityResults.TryGetValue((entry.Descriptor.Id, QualityMetric.Psnr), out var psnr) ? psnr.Average : null;
        }
        RefreshEmbeddedGraphs();
    }

    private void RefreshVideoOrder()
    {
        var selected = VideoList.SelectedItems.Cast<VideoEntry>().ToArray();
        var ordered = VideoLibrary.Sort(videos, activeSort, sortDescending);
        suppressSelection = true;
        for (var index = 0; index < ordered.Length; index++) videos.Move(videos.IndexOf(ordered[index]), index);
        VideoList.Items.Refresh();
        VideoList.SelectedItems.Clear();
        foreach (var entry in selected) VideoList.SelectedItems.Add(entry);
        suppressSelection = false;
        UpdateSelection();
    }

    private void ForwardOne_Click(object sender, RoutedEventArgs e) => ApplySeek(CurrentPosition() + TimeSpan.FromSeconds(1));
    private void ForwardFive_Click(object sender, RoutedEventArgs e) => ApplySeek(CurrentPosition() + TimeSpan.FromSeconds(5));

    private void ApplySeek(TimeSpan position)
    {
        if (left.Video is null)
            return;
        left.LastShownPosition = right.LastShownPosition = null;
        basePosition = ClampToCommon(position);
        if (comparisonActive && comparisonPanel is not null) comparisonChangeTask = comparisonPanel.SeekAsync(basePosition, isPlaying);
        ResetFpsSamples();
        if (isPlaying)
            playbackClock.Restart();
        if (left.Av1.IsOpen) left.Av1.Seek(basePosition);
        if (left.Media.Source is not null) left.Media.Position = ClampToVideo(basePosition, left.Video);
        if (right.Av1.IsOpen) right.Av1.Seek(basePosition);
        log.Record("seek_requested", "both", left.Video.Id, "position_set_not_presented",
            detail: $"target_ms={basePosition.TotalMilliseconds:F0};presented_at=unmeasured");
        SetWindowStatus("シークを要求しました。実際の提示完了時刻は未測定です。");
        UpdateTimeDisplay();
    }

    private async void FrameStep_Click(object sender, RoutedEventArgs e) => await MoveFrameAsync(1);
    private async void FrameBack_Click(object sender, RoutedEventArgs e) => await MoveFrameAsync(-1);

    private async Task MoveFrameAsync(int direction)
    {
        PausePlayback();
        if (left.Video is not { } video)
            return;
        if (comparisonActive)
        {
            ApplySeek(CurrentPosition() + TimeSpan.FromSeconds(direction / video.AverageFrameRate));
            return;
        }
        var token = frameTimesCancellation.Token;
        IReadOnlyList<TimeSpan>? times = null;
        if (probe is not null)
        {
            SetWindowStatus("フレーム時刻を取得中...");
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                times = await probe.ReadFrameTimesAsync(video.Path, CurrentPosition(), timeout.Token);
                if (token.IsCancellationRequested || left.Video?.Id != video.Id)
                    return;
            }
            catch (OperationCanceledException)
            {
                if (token.IsCancellationRequested)
                    return;
                SetWindowStatus("フレーム時刻の取得が時間切れになりました。");
            }
            catch (Exception exception)
            {
                SetWindowStatus("フレーム時刻を取得できません: " + exception.GetType().Name);
            }
        }

        var current = CurrentPosition();
        var next = direction > 0 ? times?.FirstOrDefault(time => time > current + TimeSpan.FromMilliseconds(1))
            : times?.Where(time => time < current - TimeSpan.FromMilliseconds(1)).Select(time => (TimeSpan?)time).LastOrDefault();
        if (next is not null && (direction > 0 ? next.Value > current : next.Value < current))
        {
            ApplySeek(next.Value);
            SetWindowStatus(direction > 0 ? "次のソースPTSへ移動しました。" : "前のソースPTSへ移動しました。");
        }
        else if (video.AverageFrameRate > 0)
        {
            ApplySeek(current + TimeSpan.FromSeconds(direction / video.AverageFrameRate));
            SetWindowStatus("平均fpsから1コマ分を近似しました。");
        }
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (initialized)
            LeftMedia.Volume = VolumeSlider.Value;
    }

    private void MuteCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (initialized)
            LeftMedia.IsMuted = MuteCheck.IsChecked == true;
        RightMedia.IsMuted = true;
    }

    private void DisplayTimer_Tick(object? sender, EventArgs e)
    {
        if (comparisonActive)
        {
            UpdateResourceSample();
            UpdatePerformanceDisplay();
            return;
        }
        SamplePlaybackFps(left);
        SamplePlaybackFps(right);
        UpdateResourceSample();
        if (isPlaying && CurrentPosition() >= CommonDuration() && CommonDuration() > TimeSpan.Zero)
        {
            StopPlayback();
            return;
        }

        if (isPlaying && left.Ready && right.Ready)
        {
            // Position差は提示PTS差ではない。再同期は持続した大きなずれだけに限定する。
            var difference = (SlotPosition(left) - SlotPosition(right)).Duration();
            if (difference >= TimeSpan.FromMilliseconds(100))
            {
                driftStart ??= DateTimeOffset.UtcNow;
                if (DateTimeOffset.UtcNow - driftStart >= TimeSpan.FromSeconds(1))
                {
                    if (right.Av1.IsOpen)
                        right.Av1.Seek(ClampToVideo(CurrentPosition(), right.Video!));
                    else
                        right.Media.Position = ClampToVideo(CurrentPosition(), right.Video!);
                    right.FpsSampleStamp = 0;
                    right.PlaybackFps = null;
                    log.Record("resync", "right", right.Video?.Id, "position_estimate",
                        detail: $"position_difference_ms={difference.TotalMilliseconds:F0};presented_pts_difference=unmeasured");
                    driftStart = null;
                }
            }
            else
            {
                driftStart = null;
            }
        }
        UpdatePerformanceDisplay();
    }

    private void ResetFpsSamples()
    {
        foreach (var slot in new[] { left, right })
        {
            slot.FpsSampleStamp = 0;
            slot.PlaybackFps = null;
        }
    }

    private void SamplePlaybackFps(SlotState slot)
    {
        if (!isPlaying || !slot.Ready || slot.Video is not { AverageFrameRate: > 0 } video)
            return;

        var now = Stopwatch.GetTimestamp();
        var position = SlotPosition(slot);
        if (slot.FpsSampleStamp == 0)
        {
            slot.FpsSampleStamp = now;
            slot.FpsSamplePosition = position;
            return;
        }

        var seconds = Stopwatch.GetElapsedTime(slot.FpsSampleStamp, now).TotalSeconds;
        if (seconds < 1)
            return;
        var advanced = (position - slot.FpsSamplePosition).TotalSeconds;
        // 再生位置の進み方からの換算値。実際に提示されたフレーム数ではない。
        slot.PlaybackFps = advanced >= 0 && advanced <= seconds * 2
            ? advanced / seconds * video.AverageFrameRate : null;
        slot.FpsSampleStamp = now;
        slot.FpsSamplePosition = position;
    }

    private void UpdateResourceSample()
    {
        var now = Stopwatch.GetTimestamp();
        if (previousSampleStamp != 0 && Stopwatch.GetElapsedTime(previousSampleStamp, now) < TimeSpan.FromSeconds(1))
            return;

        using var process = Process.GetCurrentProcess();
        var totalCpuTime = process.TotalProcessorTime;
        if (previousSampleStamp != 0)
        {
            var seconds = Stopwatch.GetElapsedTime(previousSampleStamp, now).TotalSeconds;
            cpuPercent = Math.Clamp(100 * (totalCpuTime - previousCpuTime).TotalSeconds /
                (seconds * Environment.ProcessorCount), 0, 100);
        }
        previousSampleStamp = now;
        previousCpuTime = totalCpuTime;
        memoryMb = process.WorkingSet64 / 1024d / 1024d;
        var video = left.Video;
        log.RecordPerformance("both", video?.Id, video?.AverageFrameRate,
            video is null ? null : $"{video.Width}x{video.Height}", video?.Codec, cpuPercent, memoryMb);
    }

    private TimeSpan CurrentPosition()
    {
        if (comparisonActive && comparisonPanel is not null) return comparisonPanel.Position;
        if (!isPlaying)
            return basePosition;
        if (left.Ready && left.Video?.HasAudio == true)
            return ClampToCommon(SlotPosition(left));
        return ClampToCommon(basePosition + playbackClock.Elapsed);
    }

    private TimeSpan SlotPosition(SlotState slot) =>
        comparisonActive && comparisonPanel is not null ? comparisonPanel.Position :
        slot.Av1.IsOpen ? slot.Av1.Position : slot.Media.Position;

    private TimeSpan CommonDuration()
    {
        if (left.Video is null)
            return TimeSpan.Zero;
        if (right.Video is null || right.Video.Duration == TimeSpan.Zero)
            return left.Video.Duration;
        if (left.Video.Duration == TimeSpan.Zero)
            return right.Video.Duration;
        return left.Video.Duration < right.Video.Duration ? left.Video.Duration : right.Video.Duration;
    }

    private TimeSpan ClampToCommon(TimeSpan time)
    {
        var duration = CommonDuration();
        if (time < TimeSpan.Zero)
            return TimeSpan.Zero;
        return duration > TimeSpan.Zero && time > duration ? duration : time;
    }

    private static TimeSpan ClampToVideo(TimeSpan time, VideoDescriptor video) =>
        video.Duration > TimeSpan.Zero && time > video.Duration ? video.Duration : time;

    // 操作イベントは即時反映し、フレーム通知は同じ入口へまとめる。
    private void UpdateTimeDisplay() => UpdateFrameStatus();

    private void RequestPlaybackRefresh()
    {
        if (closing || playbackRefreshQueued) return;
        playbackRefreshQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            playbackRefreshQueued = false;
            if (!closing) UpdateTimeDisplay();
        }));
    }

    private void UpdatePerformanceDisplay()
    {
        if (!initialized) return;
        MeasurementText.Text = $"CPU {(cpuPercent.HasValue ? $"{cpuPercent.Value:F1}%" : "計測中")} / メモリ {memoryMb:F0} MiB / GPU 不明　｜　" +
            $"実提示fps: 未測定（WPF観測: 左 {FormatObservedFps(comparisonPanel?.ObservedLeftFps)} / 右 {FormatObservedFps(comparisonPanel?.ObservedRightFps)}）　｜　" +
            $"左右PTS差（CFR換算）: {(comparisonActive && comparisonPanel?.RelativePtsDifferenceMilliseconds is { } difference ? $"{difference:F1} ms" : "未測定")}　｜　" +
            $"遅延破棄: {(comparisonActive && comparisonPanel is not null ? $"{comparisonPanel.LateDroppedFrames:N0}" : "未測定")}";

    }

    private TimeSpan DisplayedPlaybackPosition() => comparisonActive && comparisonPanel is not null
        ? comparisonPanel.Position : left.LastShownPosition ?? right.LastShownPosition ?? CurrentPosition();

    private void UpdatePlaybackControls()
    {
        if (!initialized) return;
        UpdatePlayPauseButton();
        var position = pendingSeekPosition ?? DisplayedPlaybackPosition();
        qualityGraphPanel?.SetPlaybackPosition(position);
        var duration = CommonDuration();
        TimeText.Text = $"{position:hh\\:mm\\:ss} / {duration:hh\\:mm\\:ss}";
        programmaticSlider = true;
        SeekSlider.Maximum = Math.Max(1, duration.TotalSeconds);
        if (!seekSliderPointerActive && !seekSliderDragging && pendingSeekPosition is null)
            SeekSlider.Value = Math.Min(SeekSlider.Maximum, position.TotalSeconds);
        programmaticSlider = false;
    }

    private void UpdateFrameStatus()
    {
        if (!initialized) return;
        UpdatePlaybackControls();
        var framePrefix = comparisonActive ? "元フレーム位置（0始まり）: " : "フレーム位置（反映時刻から換算）: ";
        LeftFrameStatus.Text = framePrefix + FormatFramePosition(left);
        RightFrameStatus.Text = framePrefix + FormatFramePosition(right);
        if (comparisonActive) comparisonPanel?.SetFrameStatus(LeftFrameStatus.Text, RightFrameStatus.Text);
    }

    private string FormatObservedFps(double? fps) =>
        comparisonActive && isPlaying && fps.HasValue ? $"{fps.Value:F1}" : "—";

    private string FormatFramePosition(SlotState slot)
    {
        if ((!slot.Ready && !(comparisonActive && comparisonPanel?.CurrentFrameNumber is not null)) || slot.Video is not { AverageFrameRate: > 0 } video)
            return "—";
        var total = video.TotalFrames ?? (video.Duration > TimeSpan.Zero
            ? (long)Math.Round(video.Duration.TotalSeconds * video.AverageFrameRate) : 0);
        var position = comparisonActive && comparisonPanel?.CurrentFrameNumber is { } number ? number :
            Math.Max(1, (long)Math.Floor((slot.LastShownPosition ?? SlotPosition(slot)).TotalSeconds * video.AverageFrameRate) + 1);
        if (total > 0)
            position = Math.Min(position, total);
        return total > 0
            ? $"{position:N0} / {total:N0}{(video.TotalFrames is null ? "（全数推定）" : "（全数はメタデータ）")}" 
            : $"{position:N0} / 不明";
    }

    private void UpdateInfo()
    {
        LeftDetails.Text = FormatVideoDetails(left.Video);
        RightDetails.Text = FormatVideoDetails(right.Video);
    }

    private static string FormatVideoDetails(VideoDescriptor? video) => video is null ? "" :
        $"{video.Codec} / {(video.AverageBitrate is { } bitrate ? $"{bitrate / 1000.0:N0} kbps" : "— kbps")} / {video.Width}×{video.Height} / {video.AverageFrameRate:F3} fps / 音声 {(video.HasAudio ? "あり" : "なし")}";

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            {
            e.Handled = true;
            try
            {
                var folders = paths.Where(Directory.Exists).ToArray();
                var files = await Task.Run(() => VideoDropFiles.Expand(paths, () =>
                    Dispatcher.Invoke(() => !closing && MessageBox.Show(this,
                        "読み込む動画が100本以上あります。処理を続けますか？\n「いいえ」を選ぶと今回の登録を中止します。",
                        "動画の一括登録", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                        MessageBoxResult.No) == MessageBoxResult.Yes)));
                if (!closing && files.Length > 0) await AddFilesAsync(files, folders);
            }
            catch (Exception exception)
            {
                SetWindowStatus($"フォルダーを読み込めません: {exception.GetType().Name}");
            }
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (closing) return;
        if (evaluationCancellation is not null)
        {
            e.Cancel = true;
            evaluationCloseRequested = true;
            evaluationCancellation.Cancel();
            return;
        }
        closing = true;
        comparisonPanel?.Dispose();
        displayTimer.Stop();
        seekTimer.Stop();
        frameTimesCancellation.Cancel();
        frameTimesCancellation.Dispose();
        left.Media.Close();
        right.Media.Close();
        left.Av1.Dispose();
        right.Av1.Dispose();
        log.Record("session_end", "none", null, "closed");
        log.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private sealed class SlotState(string side, MediaElement media, Image av1Image, TextBlock name)
    {
        public string Side { get; } = side;
        public MediaElement Media { get; } = media;
        public Av1PreviewPlayer Av1 { get; } = new(av1Image);
        public TextBlock Name { get; } = name;
        public VideoDescriptor? Video { get; set; }
        public long Generation { get; set; }
        public bool Ready { get; set; }
        public TimeSpan? LastShownPosition { get; set; }
        public long OpenStarted { get; set; }
        public long FpsSampleStamp { get; set; }
        public TimeSpan FpsSamplePosition { get; set; }
        public double? PlaybackFps { get; set; }
    }
}
