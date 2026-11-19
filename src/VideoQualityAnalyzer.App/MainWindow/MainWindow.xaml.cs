using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
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
    private readonly EvaluationResultStore evaluationResults = new();
    private Dictionary<(string VideoId, QualityMetric Metric), QualityResult> qualityResults => evaluationResults.Results;
    private string? previousRightId;
    private long selectionGeneration;
    private TimeSpan basePosition;
    private readonly PlaybackDriftTracker playbackDrift = new();
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
        ApplyAudioSettings();
        initialized = true;
        InitializeEmbeddedGraphs();
        // FFmpeg未配置でも本体と設定画面を開けるよう、比較画面の初期化を保留する。
        if (probe is not null) InitializePreviewZoomControls();
        log.Record("session_start", "none", null, "ready", detail: "presented_fps=unmeasured;decode_mode=unknown");
        Loaded += async (_, _) =>
        {
            if (TryRunStartupVerification()) return;
            var arguments = Environment.GetCommandLineArgs();
            var verifyConcurrentAdd = arguments.Contains("--verify-concurrent-add");
            var startupFiles = arguments.Skip(1).Where(File.Exists).ToArray();
            if (startupFiles.Length > 0 && !verifyConcurrentAdd)
                await AddFilesAsync(startupFiles);
            if (arguments.Contains("--verify-main-shortcuts"))
            {
                await VerifyMainShortcutsAsync();
                return;
            }
            var graphPlaybackCheck = arguments.FirstOrDefault(arg => arg.StartsWith("--verify-main-graph-playback="));
            if (graphPlaybackCheck is not null) await VerifyMainGraphPlaybackAsync(graphPlaybackCheck.Split('=', 2)[1]);
            else if (verifyConcurrentAdd) await VerifyConcurrentAddAsync(startupFiles);
            else if (Environment.GetCommandLineArgs().Contains("--verify-graph-image-save")) await VerifyGraphImageSaveAsync();
            else if (Environment.GetCommandLineArgs().Contains("--verify-main-features")) VerifyMainFeatures();
            else if (Environment.GetCommandLineArgs().Contains("--verify-main-comparison")) await VerifyEmbeddedComparisonAsync();
            else if (Environment.GetCommandLineArgs().Contains("--verify-main-graphs")) await VerifyEmbeddedGraphsAsync();
            else if (Environment.GetCommandLineArgs().Contains("--verify-preview-controls")) await VerifyPreviewControlsAsync();
            else if (Environment.GetCommandLineArgs().Contains("--verify-main-refresh")) await VerifyPlaybackRefreshAsync();
            else if (Environment.GetCommandLineArgs().Contains("--verify-main-seek")) await VerifySeekSliderAsync();
            else if (Environment.GetCommandLineArgs().Contains("--verify-main-evaluation")) await VerifyMainEvaluationAsync();
            else if (Environment.GetCommandLineArgs().Contains("--verify-main-partial-evaluation")) await VerifyMainPartialEvaluationAsync();
            else if (Environment.GetCommandLineArgs().Contains("--verify-main-continuous-evaluation")) await VerifyMainContinuousEvaluationAsync();
            else if (Environment.GetCommandLineArgs().Contains("--verify-evaluated-playback")) await VerifyEvaluatedPlaybackAsync();
        };
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
        libraryLoads.Dispose();
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

}
