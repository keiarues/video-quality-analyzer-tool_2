using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.GpuPreview;

public partial class ComparisonPanel : UserControl, IDisposable
{
    private readonly FrameComparisonService comparison;
    private readonly string ffmpegPath;
    private readonly string ffprobePath;
    private readonly bool benchmarkMode;
    private readonly bool captureBenchmark;
    private readonly bool verifyComparison;
    private readonly bool verifySelection;
    private readonly WipeBoundaryController wipeBoundaryController;
    private WriteableBitmap? markerBitmap;
    private readonly byte[] markerPixels = new byte[48 * 48 * 4];
    private readonly string leftPath;
    private readonly string rightPath;
    private readonly HashSet<long> compositionFrames = [];
    private bool benchmarkVisible = true;
    private long benchmarkFirstFrame;
    private double benchmarkFrameRate;
    private CancellationTokenSource? loadCancellation;
    private CancellationTokenSource? playbackCancellation;
    private StreamingFramePairReader? preparedReader;
    private string? preparedLeftPath;
    private string? preparedRightPath;
    private bool lastPlaybackUsedPreparedReader;
    private long selectionGeneration;
    private Task? selectionChangeTask;
    private ComparisonFrame? currentLeft;
    private ComparisonFrame? currentRight;
    private volatile bool playbackPaused;
    private volatile bool wipeMode;
    private volatile bool twoScreenMode;
    private volatile int heatmapGain = 16;
    private volatile int heatmapBlurRadius;
    private WriteableBitmap? playingLeft;
    private WriteableBitmap? playingRight;
    private WriteableBitmap? playingHeatmap;

    private static string FormatVideoDetails(VideoDescriptor video) =>
        $"{video.Codec} / {(video.AverageBitrate is { } bitrate ? $"{bitrate / 1000.0:N0} kbps" : "— kbps")} / {video.Width}×{video.Height} / {video.AverageFrameRate:F3} fps / 音声 {(video.HasAudio ? "あり" : "なし")}";

    public ComparisonPanel(string leftPath, string rightPath, long frameNumber,
        bool showWipe = false, bool benchmarkMode = false, bool captureBenchmark = false,
        bool verifyComparison = false, bool verifySelection = false,
        IEnumerable<string>? videoPaths = null, bool showBlur = true, bool embedded = false)
    {
        InitializeComponent();
        this.embedded = embedded;
        if (embedded) CommonControls.Visibility = Visibility.Collapsed;
        if (!showBlur)
            BlurLabel.Visibility = BlurSelect.Visibility = BlurHint.Visibility = Visibility.Collapsed;
        wipeBoundaryController = new WipeBoundaryController(WipeViewport, WipeRightLayer,
            WipeBoundary, WipePositionText,
            () => currentLeft is null ? 4d / 3 : (double)currentLeft.Width / currentLeft.Height);
        this.leftPath = leftPath;
        this.rightPath = rightPath;
        this.benchmarkMode = benchmarkMode;
        this.captureBenchmark = captureBenchmark;
        this.verifyComparison = verifyComparison;
        this.verifySelection = verifySelection;
        ffmpegPath = FfmpegRgbReader.FindExecutable() ??
            throw new FileNotFoundException("FFmpegが見つかりません。");
        ffprobePath = FfprobeService.FindExecutable() ??
            throw new FileNotFoundException("FFprobeが見つかりません。");
        comparison = new FrameComparisonService(ffmpegPath, ffprobePath);
        var workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var paths = (videoPaths ?? (Directory.Exists(Path.Combine(workspace, "test-data"))
            ? Directory.EnumerateFiles(Path.Combine(workspace, "test-data")) : []))
            .Where(path => Path.GetExtension(path).ToLowerInvariant() is ".mkv" or ".mp4")
            .Append(leftPath).Append(rightPath).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var path in paths)
        {
            LeftSelect.Items.Add(new VideoChoice(Path.GetFileName(path), path));
            RightSelect.Items.Add(new VideoChoice(Path.GetFileName(path), path));
        }
        LeftSelect.SelectedItem = LeftSelect.Items.Cast<VideoChoice>()
            .First(choice => string.Equals(choice.Path, leftPath, StringComparison.OrdinalIgnoreCase));
        RightSelect.SelectedItem = RightSelect.Items.Cast<VideoChoice>()
            .First(choice => string.Equals(choice.Path, rightPath, StringComparison.OrdinalIgnoreCase));
        FrameNumberBox.Text = frameNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
        GainSelect.SelectedIndex = 1;
        BlurSelect.SelectedIndex = 0;
        ViewSelect.SelectedIndex = showWipe ? 1 : (showBlur ? 2 : 0);
        UpdateWipeBoundary();
        ResultText.Text = "左右に同じ元フレーム番号を指定して比較します。再生中も表示を切り替えられます。";
        LeftSelect.SelectionChanged += VideoSelectionChanged;
        RightSelect.SelectionChanged += VideoSelectionChanged;
        Loaded += (_, _) =>
        {
            if (captureBenchmark && Window.GetWindow(this) is { } host)
            {
                host.Left = 0;
                host.Top = 0;
                host.Topmost = true;
            }
            if (embedded) return;
            if (benchmarkMode)
            {
                if (captureBenchmark) PrepareCaptureMarker();
                CompositionTarget.Rendering += ObserveComposition;
                PlayPause_Click(this, new RoutedEventArgs());
            }
            else if (verifyComparison) _ = VerifyComparisonAsync(frameNumber);
            else if (verifySelection) _ = VerifySelectionAsync(frameNumber);
            else _ = LoadComparisonAsync();
        };
    }

    private readonly bool embedded;
    private bool configuring;
    private bool resumeRequested;
    private double sourceFrameRate = 30;
    private TimeSpan requestedPosition;
    public event Action? PlaybackEnded;
    public event Action? FrameShown;
    public event Action<string, string>? SourcesRequested;
    public bool IsPlaying => playbackCancellation is not null && !playbackPaused;
    public bool IsReady => currentLeft is not null && currentRight is not null;
    public void SetFrameStatus(string left, string right)
    {
        LeftFrameStatus.Text = left;
        RightFrameStatus.Text = right;
    }

    public TimeSpan Position => currentLeft is null ? requestedPosition : TimeSpan.FromSeconds(currentLeft.FrameNumber / sourceFrameRate);
    public long? CurrentFrameNumber => currentLeft?.FrameNumber;
    public void Play() { resumeRequested = true; if (IsReady && !IsPlaying) PlayPause_Click(this, new RoutedEventArgs()); }
    public void Pause() { resumeRequested = false; if (IsPlaying) PlayPause_Click(this, new RoutedEventArgs()); }
    public int SelectedModeIndex => ViewSelect.SelectedIndex;
    public void SelectMode(int index) => ViewSelect.SelectedIndex = index;

    public void Clear(TimeSpan position)
    {
        ++selectionGeneration;
        resumeRequested = false;
        StopPlayback();
        loadCancellation?.Cancel();
        currentLeft = currentRight = null;
        requestedPosition = position;
        var reader = preparedReader;
        preparedReader = null;
        if (reader is not null) _ = reader.DisposeAsync();
        LeftImage.Source = RightImage.Source = HeatmapImage.Source = null;
        WipeLeftImage.Source = WipeRightImage.Source = null;
    }

    public void SelectPreviewSource(bool left, string path)
    {
        var selector = SelectedModeIndex == 1 ? (left ? WipeLeftPreviewSelect : WipeRightPreviewSelect)
            : (left ? LeftPreviewSelect : RightPreviewSelect);
        selector.SelectedItem = selector.Items.Cast<VideoChoice>().First(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    public void SetVideoChoices(IEnumerable<string> paths)
    {
        configuring = true;
        try
        {
            var allowed = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var selector in new[] { LeftSelect, RightSelect })
            {
                foreach (var item in selector.Items.Cast<VideoChoice>().Where(item => !allowed.Contains(item.Path)).ToArray()) selector.Items.Remove(item);
                foreach (var path in allowed)
                    if (!selector.Items.Cast<VideoChoice>().Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase)))
                        selector.Items.Add(new VideoChoice(Path.GetFileName(path), path));
            }
        }
        finally { configuring = false; }
    }

    public async Task SetSourcesAsync(VideoDescriptor left, VideoDescriptor right, TimeSpan position, bool resume)
    {
        configuring = true;
        try
        {
            foreach (var path in new[] { left.Path, right.Path })
            {
                if (!LeftSelect.Items.Cast<VideoChoice>().Any(choice => string.Equals(choice.Path, path, StringComparison.OrdinalIgnoreCase)))
                {
                    LeftSelect.Items.Add(new VideoChoice(Path.GetFileName(path), path));
                    RightSelect.Items.Add(new VideoChoice(Path.GetFileName(path), path));
                }
            }
            LeftSelect.SelectedItem = LeftSelect.Items.Cast<VideoChoice>().First(choice => string.Equals(choice.Path, left.Path, StringComparison.OrdinalIgnoreCase));
            RightSelect.SelectedItem = RightSelect.Items.Cast<VideoChoice>().First(choice => string.Equals(choice.Path, right.Path, StringComparison.OrdinalIgnoreCase));
            sourceFrameRate = left.AverageFrameRate;
            LeftDetails.Text = FormatVideoDetails(left);
            RightDetails.Text = FormatVideoDetails(right);
            LeftFrameLabel.Text = "左: " + left.Name;
            RightFrameLabel.Text = "右: " + right.Name;
            LeftFrameLabel.ToolTip = left.Name;
            RightFrameLabel.ToolTip = right.Name;
        }
        finally { configuring = false; }
        await SeekAsync(position, resume);
    }

    public async Task SeekAsync(TimeSpan position, bool resume)
    {
        var generation = ++selectionGeneration;
        StopPlayback(keepRequestedFrame: true);
        requestedPosition = position;
        resumeRequested = resume;
        currentLeft = currentRight = null;
        LeftImage.Source = RightImage.Source = HeatmapImage.Source = null;
        WipeLeftImage.Source = WipeRightImage.Source = null;
        FrameNumberBox.Text = Math.Max(0, (long)Math.Round(position.TotalSeconds * sourceFrameRate))
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        await LoadComparisonAsync();
        if (generation == selectionGeneration && IsReady && resumeRequested) Play();
    }

    public async Task VerifyCurrentPixelsAsync()
    {
        var number = CheckAlignedPair();
        var left = (VideoChoice)LeftSelect.SelectedItem;
        var right = (VideoChoice)RightSelect.SelectedItem;
        var expected = await Task.WhenAll(comparison.ReadFrameAsync(left.Path, number, CancellationToken.None),
            comparison.ReadFrameAsync(right.Path, number, CancellationToken.None));
        if (currentLeft is null || currentRight is null ||
            !currentLeft.Pixels.AsSpan().SequenceEqual(expected[0].Pixels) ||
            !currentRight.Pixels.AsSpan().SequenceEqual(expected[1].Pixels))
            throw new InvalidOperationException("主画面の左右フレームが静止復号と一致しません。");
    }

    private void VideoSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (configuring || !IsLoaded || LeftSelect.SelectedItem is null || RightSelect.SelectedItem is null) return;
        selectionChangeTask = SwitchSelectedVideosAsync();
    }

    private async Task SwitchSelectedVideosAsync()
    {
        var generation = ++selectionGeneration;
        if (embedded && SourcesRequested is not null)
        {
            SourcesRequested.Invoke(((VideoChoice)LeftSelect.SelectedItem).Path, ((VideoChoice)RightSelect.SelectedItem).Path);
            return;
        }
        var frameNumber = currentLeft?.FrameNumber ??
            (long.TryParse(FrameNumberBox.Text, out var requested) ? requested : 0);
        var resume = playbackCancellation is not null && !playbackPaused;
        StopPlayback(keepRequestedFrame: true);
        FrameNumberBox.Text = frameNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
        currentLeft = null;
        currentRight = null;
        playingLeft = playingRight = playingHeatmap = null;
        LeftImage.Source = RightImage.Source = HeatmapImage.Source = null;
        WipeLeftImage.Source = WipeRightImage.Source = null;
        await LoadComparisonAsync();
        if (generation == selectionGeneration && resume && preparedReader is not null &&
            currentLeft?.FrameNumber == frameNumber && currentRight?.FrameNumber == frameNumber)
            PlayPause_Click(this, new RoutedEventArgs());
    }

    private void SeekPlay_Click(object sender, RoutedEventArgs e)
    {
        var requested = FrameNumberBox.Text;
        StopPlayback(keepRequestedFrame: true);
        FrameNumberBox.Text = requested;
        PlayPause_Click(sender, e);
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (playbackCancellation is not null)
        {
            playbackPaused = !playbackPaused;
            PlayPauseButton.Content = playbackPaused ? "再生" : "一時停止";
            return;
        }
        if (LeftSelect.SelectedItem is not VideoChoice left ||
            RightSelect.SelectedItem is not VideoChoice right ||
            !long.TryParse(FrameNumberBox.Text, out var frameNumber) || frameNumber < 0)
        {
            ResultText.Text = "0以上の元フレーム番号と左右の動画を指定してください。";
            return;
        }
        loadCancellation?.Cancel();
        StreamingFramePairReader? reusedReader = null;
        (ComparisonFrame Left, ComparisonFrame Right)? firstPair = null;
        if (preparedReader is not null)
        {
            if (string.Equals(preparedLeftPath, left.Path, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(preparedRightPath, right.Path, StringComparison.OrdinalIgnoreCase) &&
                currentLeft?.FrameNumber == frameNumber && currentRight?.FrameNumber == frameNumber)
            {
                reusedReader = preparedReader;
                firstPair = (currentLeft, currentRight);
            }
            else _ = preparedReader.DisposeAsync();
            preparedReader = null;
        }
        playbackPaused = false;
        PlayPauseButton.Content = "一時停止";
        var cancellation = new CancellationTokenSource();
        playbackCancellation = cancellation;
        lastPlaybackUsedPreparedReader = reusedReader is not null;
        _ = PlayAsync(left.Path, right.Path, frameNumber, cancellation, reusedReader, firstPair);
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => StopPlayback();

    private void StopPlayback(bool keepRequestedFrame = false)
    {
        var wasPlaying = playbackCancellation is not null;
        playbackCancellation?.Cancel();
        playbackCancellation = null;
        playbackPaused = false;
        PlayPauseButton.Content = "再生";
        if (!keepRequestedFrame && currentLeft is not null)
            FrameNumberBox.Text = currentLeft.FrameNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (wasPlaying && currentLeft is not null)
            ResultText.Text = $"比較再生を停止しました。表示中の元フレーム #{currentLeft.FrameNumber}。";
    }

    private async Task PlayAsync(string leftPath, string rightPath, long firstFrameNumber,
        CancellationTokenSource cancellation, StreamingFramePairReader? reusedReader = null,
        (ComparisonFrame Left, ComparisonFrame Right)? firstPair = null)
    {
        var token = cancellation.Token;
        ResultText.Text = "連続復号を準備中…";
        try
        {
            await Task.Run(async () =>
            {
            await using var reader = reusedReader ?? await StreamingFramePairReader.OpenAsync(ffmpegPath,
                ffprobePath, leftPath, rightPath, firstFrameNumber, token);
            var frameRate = reader.Descriptor.AverageFrameRate;
            var firstMeasured = firstFrameNumber + (long)Math.Ceiling(2 * frameRate);
            var afterMeasured = firstFrameNumber + (long)Math.Ceiling(32 * frameRate);
            if (benchmarkMode)
                await Dispatcher.InvokeAsync(() =>
                {
                    benchmarkFirstFrame = firstFrameNumber;
                    benchmarkFrameRate = frameRate;
                });
            var clock = new Stopwatch();
            var displayed = 0;
            var skipped = 0;
            var measuredSubmitted = new List<long>();
            var measuredLateDropped = new List<long>();
            var pendingPair = firstPair;
            while (!token.IsCancellationRequested)
            {
                if (playbackPaused)
                {
                    clock.Stop();
                    await Task.Delay(20, token);
                    continue;
                }
                var pair = pendingPair ?? await reader.ReadNextAsync(token);
                pendingPair = null;
                if (pair is null) break;
                while (playbackPaused)
                {
                    clock.Stop();
                    await Task.Delay(20, token);
                }
                clock.Start();
                var (left, right) = pair.Value;
                if (benchmarkMode && left.FrameNumber >= afterMeasured) break;
                var due = TimeSpan.FromSeconds((left.FrameNumber - firstFrameNumber) / frameRate);
                var wait = due - clock.Elapsed;
                if (wait > TimeSpan.Zero) await Task.Delay(wait, token);
                if (clock.Elapsed - due > TimeSpan.FromMilliseconds(100))
                {
                    skipped++;
                    if (benchmarkMode && left.FrameNumber >= firstMeasured)
                        measuredLateDropped.Add(left.FrameNumber);
                    continue;
                }
                var blurRadius = heatmapBlurRadius;
                var difference = wipeMode || twoScreenMode ? null : FrameComparisonService.CreateHeatmap(left, right,
                    heatmapGain, blurRadius);
                var presented = false;
                await Dispatcher.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested || playbackPaused) return;
                    currentLeft = left;
                    currentRight = right;
                    UpdatePlayingBitmaps(left, right, difference);
                    FrameShown?.Invoke();
                    presented = true;
                    if (captureBenchmark) UpdateCaptureMarker(left.FrameNumber);
                    displayed++;
                    if (benchmarkMode && left.FrameNumber >= firstMeasured)
                        measuredSubmitted.Add(left.FrameNumber);
                    if (displayed % 6 == 0)
                    {
                        FrameNumberBox.Text = left.FrameNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        ResultText.Text = $"再生比較 #{left.FrameNumber} / 描画要求 {displayed / Math.Max(0.001, clock.Elapsed.TotalSeconds):F1} fps" +
                            $" / 遅延破棄 {skipped}枚" +
                            (difference is null ? "" :
                                $" / {(blurRadius == 0 ? "平均RGB差" : "ぼかし後平均RGB差")} {difference.MeanAbsoluteRgbDifference:F2}") +
                            "。実画面提示fpsは未測定。";
                    }
                });
                if (!presented && !token.IsCancellationRequested) pendingPair = pair;
            }
            if (benchmarkMode && !token.IsCancellationRequested)
            {
                await Task.Delay(150, token);
                using var process = Process.GetCurrentProcess();
                var appCpuSeconds = process.TotalProcessorTime.TotalSeconds;
                var appPeakBytes = process.PeakWorkingSet64;
                var decoderCpuSeconds = reader.DecoderCpuSeconds;
                var decoderPeakBytes = reader.DecoderPeakWorkingSetBytes;
                await Dispatcher.InvokeAsync(() => SaveBenchmark(frameRate, firstMeasured,
                    afterMeasured, measuredSubmitted, measuredLateDropped, clock.Elapsed,
                    appCpuSeconds, appPeakBytes, decoderCpuSeconds, decoderPeakBytes));
                return;
            }
            if (!token.IsCancellationRequested)
                await Dispatcher.InvokeAsync(() => ResultText.Text =
                    $"再生比較終了 / 描画要求 {displayed}枚 / 遅延破棄 {skipped}枚。実画面提示fpsは未測定。");
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (!token.IsCancellationRequested)
            {
                ResultText.Text = $"再生比較に失敗しました（0x{exception.HResult:X8} {exception.Message}）。";
                if (benchmarkMode) Application.Current.Shutdown(1);
            }
        }
        finally
        {
            if (ReferenceEquals(playbackCancellation, cancellation))
            {
                playbackCancellation = null;
                PlayPauseButton.Content = "再生";
                PlaybackEnded?.Invoke();
            }
            cancellation.Dispose();
        }
    }

    private int SelectedGain() => GainSelect.SelectedItem is ComboBoxItem { Tag: string text } &&
        int.TryParse(text, out var value) ? value : 16;

    private void UpdatePlayingBitmaps(ComparisonFrame left, ComparisonFrame right, FrameDifferenceResult? difference)
    {
        if (playingLeft is null || playingLeft.PixelWidth != left.Width || playingLeft.PixelHeight != left.Height)
        {
            playingLeft = CreateWritable(left.Width, left.Height);
            playingRight = CreateWritable(right.Width, right.Height);
            playingHeatmap = CreateWritable(left.Width, left.Height);
            LeftImage.Source = WipeLeftImage.Source = playingLeft;
            RightImage.Source = WipeRightImage.Source = playingRight;
            HeatmapImage.Source = playingHeatmap;
        }
        var bounds = new Int32Rect(0, 0, left.Width, left.Height);
        playingLeft.WritePixels(bounds, left.Pixels, left.Width * 4, 0);
        playingRight!.WritePixels(bounds, right.Pixels, right.Width * 4, 0);
        if (difference is not null)
        {
            HeatmapImage.Source = playingHeatmap;
            playingHeatmap!.WritePixels(bounds, difference.Heatmap, difference.Width * 4, 0);
        }
        UpdateWipeBoundary();
    }

    private static WriteableBitmap CreateWritable(int width, int height) =>
        new(width, height, 96, 96, PixelFormats.Bgra32, null);

    private void PrepareCaptureMarker()
    {
        markerBitmap = CreateWritable(48, 48);
        BenchmarkMarker.Source = markerBitmap;
        BenchmarkMarker.Visibility = Visibility.Visible;
        UpdateLayout();
        var point = BenchmarkMarker.PointToScreen(new Point(0, 0));
        var opposite = BenchmarkMarker.PointToScreen(new Point(48, 48));
        var workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var directory = Path.Combine(workspace, "measurement-results");
        Directory.CreateDirectory(directory);
        var target = new
        {
            ProcessId = Environment.ProcessId,
            X = (int)Math.Round(point.X),
            Y = (int)Math.Round(point.Y),
            Width = (int)Math.Round(opposite.X - point.X),
            Height = (int)Math.Round(opposite.Y - point.Y),
            Mode = wipeMode ? "wipe" : "heatmap"
        };
        File.WriteAllText(Path.Combine(directory, $"comparison-capture-target-{Environment.ProcessId}.json"),
            JsonSerializer.Serialize(target));
    }

    private void UpdateCaptureMarker(long frameNumber)
    {
        if (markerBitmap is null) return;
        for (var y = 0; y < 48; y++)
        {
            for (var x = 0; x < 48; x++)
            {
                var bit = (y / 12) * 4 + x / 12;
                var color = (frameNumber & (1L << bit)) != 0 ? (byte)255 : (byte)24;
                var offset = (y * 48 + x) * 4;
                markerPixels[offset] = markerPixels[offset + 1] = markerPixels[offset + 2] = color;
                markerPixels[offset + 3] = 255;
            }
        }
        markerBitmap.WritePixels(new Int32Rect(0, 0, 48, 48), markerPixels, 48 * 4, 0);
    }

    private void ObserveComposition(object? sender, EventArgs e)
    {
        if (!benchmarkMode || benchmarkFrameRate <= 0) return;
        if (!IsVisible || Window.GetWindow(this)?.WindowState == WindowState.Minimized)
            benchmarkVisible = false;
        if (currentLeft is null) return;
        var number = currentLeft.FrameNumber;
        var first = benchmarkFirstFrame + (long)Math.Ceiling(2 * benchmarkFrameRate);
        var after = benchmarkFirstFrame + (long)Math.Ceiling(32 * benchmarkFrameRate);
        if (number >= first && number < after)
            compositionFrames.Add(number);
    }

    private void SaveBenchmark(double frameRate, long first, long after,
        IReadOnlyList<long> submitted, IReadOnlyList<long> lateDropped, TimeSpan elapsed,
        double appCpuSeconds, long appPeakBytes, double decoderCpuSeconds, long decoderPeakBytes)
    {
        CompositionTarget.Rendering -= ObserveComposition;
        var expected = after - first;
        var composed = compositionFrames.Count;
        var report = new
        {
            SchemaVersion = 1,
            RecordedAt = DateTimeOffset.Now,
            Mode = wipeMode ? "wipe" : "heatmap",
            LeftVideo = Path.GetFileName(leftPath),
            RightVideo = Path.GetFileName(rightPath),
            FrameRate = frameRate,
            MeasuredFirstFrame = first,
            MeasuredAfterLastFrame = after,
            ExpectedFrames = expected,
            SubmittedFrames = submitted.Count,
            SubmittedMissing = expected - submitted.Count,
            LateDroppedFrames = lateDropped.Count,
            CompositionObservedUniqueFrames = composed,
            CompositionMissing = expected - composed,
            CompositionMissingRate = (double)(expected - composed) / expected,
            CompositionWithinThreePercent = FrameDropCriteria.MeetsThreePercent(expected, composed),
            WindowVisibleThroughout = benchmarkVisible,
            PlaybackElapsedSeconds = elapsed.TotalSeconds,
            AppCpuSeconds = appCpuSeconds,
            DecoderCpuSeconds = decoderCpuSeconds,
            AppPeakWorkingSetMb = appPeakBytes / 1048576.0,
            DecoderPeakWorkingSetMb = decoderPeakBytes / 1048576.0,
            GpuUtilizationMeasured = false,
            ActualScreenPresentationMeasured = false,
            ExternalCaptureMarkerEnabled = captureBenchmark,
            Note = "CompositionTarget.Renderingで観測した異なる元フレーム。画面への実提示を証明する値ではない。"
        };
        var workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var directory = Path.Combine(workspace, "measurement-results");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, $"comparison-{report.Mode}-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Window.GetWindow(this)?.Close();
    }

    private async Task VerifyComparisonAsync(long firstFrame)
    {
        var operation = "開始";
        try
        {
            await LoadComparisonAsync();
            if (preparedReader is null || CheckAlignedPair() != firstFrame)
                throw new InvalidOperationException("静止表示の復号器を再生へ引き継げません。");
            PlayPause_Click(this, new RoutedEventArgs());
            var initialPlaybackReusedDecoder = lastPlaybackUsedPreparedReader;
            if (!initialPlaybackReusedDecoder)
                throw new InvalidOperationException("再生時に復号器を再利用していません。");
            await WaitForFrameAsync(firstFrame + 15);
            operation = "一時停止";
            PlayPause_Click(this, new RoutedEventArgs());
            await Task.Delay(100);
            var pausedFrame = CheckAlignedPair();
            await Task.Delay(500);
            if (CheckAlignedPair() != pausedFrame)
                throw new InvalidOperationException("一時停止中にフレームが進みました。");

            operation = "ワイプ境界";
            ViewSelect.SelectedIndex = 1;
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Loaded);
            UpdateLayout();
            foreach (var percent in new[] { 0d, 25d, 50d, 75d, 100d })
            {
                wipeBoundaryController.SetFromViewportX(wipeBoundaryController.ImageLeft +
                    wipeBoundaryController.ImageWidth * percent / 100);
                if (WipeRightLayer.Clip is not RectangleGeometry clip ||
                    wipeBoundaryController.ImageWidth <= 0 ||
                    Math.Abs(wipeBoundaryController.Fraction * 100 - percent) > 0.001 ||
                    Math.Abs(clip.Rect.Left - WipeBoundary.Margin.Left - WipeBoundary.Width / 2) > 0.5 ||
                    WipeBoundary.Visibility != Visibility.Visible)
                    throw new InvalidOperationException("ワイプ境界と切り替え位置が一致しません。");
            }
            ViewSelect.SelectedIndex = 0;
            UpdateLayout();
            if (ViewSelect.Items.Count != 3 || ViewSelect.Items[0] != TwoScreenTab || ViewSelect.Items[1] != WipeTab ||
                ViewSelect.Items[2] != HeatmapTab || ViewSelect.SelectedItem != TwoScreenTab || HeatmapOptions.IsVisible)
                throw new InvalidOperationException("2画面タブに専用外のオプションが表示されています。");
            if (HeatmapPanel.Visibility != Visibility.Collapsed || HeatmapColumn.Width.Value != 0)
                throw new InvalidOperationException("2画面表示に切り替わりません。");
            CheckAlignedPair();
            ViewSelect.SelectedIndex = 2;
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Loaded);
            UpdateLayout();
            if (!HeatmapOptions.IsVisible || !HeatmapPanel.IsAncestorOf(GainSelect) ||
                HeatmapTab.Content is not DockPanel optionsPanel || !optionsPanel.Children.Contains(HeatmapOptions))
                throw new InvalidOperationException($"ヒートマップ専用オプションがタブ内にありません。表示={HeatmapOptions.IsVisible} 配置={HeatmapPanel.IsAncestorOf(GainSelect)} 内容={HeatmapTab.Content?.GetType().Name}");
            var snapshot = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
            snapshot.Render((Visual)Content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(snapshot));
            var layoutPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "../../../../../measurement-results/comparison-tabs-layout.png"));
            Directory.CreateDirectory(Path.GetDirectoryName(layoutPath)!);
            using (var stream = File.Create(layoutPath)) encoder.Save(stream);

            operation = "一時停止中のぼかし切り替え";
            for (var index = 0; index < BlurSelect.Items.Count; index++)
            {
                BlurSelect.SelectedIndex = index;
                CheckDisplayedHeatmap(SelectedBlurRadius());
            }

            operation = "再開";
            PlayPause_Click(this, new RoutedEventArgs());
            await WaitForFrameAsync(pausedFrame + 10);
            var resumedFrame = CheckAlignedPair();
            operation = "再生中の強いぼかし";
            CheckDisplayedHeatmap(4);
            BlurSelect.SelectedIndex = 0;

            operation = "再生中シーク";
            FrameNumberBox.Text = "450";
            SeekPlay_Click(this, new RoutedEventArgs());
            await WaitForFrameAsync(450);
            PlayPause_Click(this, new RoutedEventArgs());
            await Task.Delay(100);
            var seekedFrame = CheckAlignedPair();
            await Task.Delay(500);
            if (CheckAlignedPair() != seekedFrame)
                throw new InvalidOperationException("シーク後の一時停止中にフレームが進みました。");

            operation = "静止復号との画素照合";
            var expectedFrames = await Task.WhenAll(
                comparison.ReadFrameAsync(leftPath, seekedFrame, CancellationToken.None),
                comparison.ReadFrameAsync(rightPath, seekedFrame, CancellationToken.None));
            if (currentLeft?.FrameNumber != seekedFrame || currentRight?.FrameNumber != seekedFrame ||
                !currentLeft.Pixels.AsSpan().SequenceEqual(expectedFrames[0].Pixels) ||
                !currentRight.Pixels.AsSpan().SequenceEqual(expectedFrames[1].Pixels))
                throw new InvalidOperationException("シーク後の左右画素が静止復号と一致しません。");

            operation = "停止";
            StopPlayback();
            await Task.Delay(300);
            if (CheckAlignedPair() != seekedFrame)
                throw new InvalidOperationException("停止後にフレームが変わりました。");
            WriteVerificationResult("comparison-operations", new
            {
                LeftVideo = Path.GetFileName(leftPath), RightVideo = Path.GetFileName(rightPath),
                FirstFrame = firstFrame, PausedFrame = pausedFrame,
                ResumedFrame = resumedFrame, SeekedFrame = seekedFrame,
                InitialPlaybackReusedDecoder = initialPlaybackReusedDecoder,
                WipeBoundaryAligned = true, HeatmapBlurLevelsMatched = true,
                SeekedPixelsMatchStaticDecode = true,
                PausedAndStoppedFrameStable = true, Result = "合格"
            });
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"比較操作: {operation} / 理由: {exception.Message} / タブ {ViewSelect.SelectedIndex} / ワイプ領域 {WipeViewport.ActualWidth}×{WipeViewport.ActualHeight} / 元画像幅 {wipeBoundaryController.ImageWidth}");
            WriteVerificationResult("comparison-operations-failed", new
            {
                LeftVideo = Path.GetFileName(leftPath), RightVideo = Path.GetFileName(rightPath),
                FailedOperation = operation, ErrorType = exception.GetType().Name,
                ErrorCode = $"0x{exception.HResult:X8}", Result = "失敗"
            });
            Application.Current.Shutdown(1);
        }
        finally { Window.GetWindow(this)?.Close(); }
    }

    private async Task VerifySelectionAsync(long firstFrame)
    {
        var operation = "初期表示";
        try
        {
            await LoadComparisonAsync();
            if (CheckAlignedPair() != firstFrame) throw new InvalidDataException("初期フレームが異なります。");
            var original = (VideoChoice)RightSelect.SelectedItem;
            var originalLeft = (VideoChoice)LeftSelect.SelectedItem;
            var alternate = RightSelect.Items.Cast<VideoChoice>().First(choice =>
                !string.Equals(choice.Path, original.Path, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(choice.Path, ((VideoChoice)LeftSelect.SelectedItem).Path,
                    StringComparison.OrdinalIgnoreCase));

            operation = "停止中の選択変更";
            RightSelect.SelectedItem = alternate;
            if (selectionChangeTask is not null) await selectionChangeTask;
            if (CheckAlignedPair() != firstFrame || preparedRightPath != alternate.Path)
                throw new InvalidDataException("新しい右動画のフレームが表示されません。");
            var expected = await comparison.ReadFrameAsync(alternate.Path, firstFrame, CancellationToken.None);
            if (!currentRight!.Pixels.AsSpan().SequenceEqual(expected.Pixels))
                throw new InvalidDataException("新しい右動画の画素が一致しません。");

            operation = "左動画の選択変更";
            var alternateLeft = LeftSelect.Items.Cast<VideoChoice>().First(choice =>
                !string.Equals(choice.Path, originalLeft.Path, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(choice.Path, alternate.Path, StringComparison.OrdinalIgnoreCase));
            LeftSelect.SelectedItem = alternateLeft;
            if (selectionChangeTask is not null) await selectionChangeTask;
            if (CheckAlignedPair() != firstFrame || preparedLeftPath != alternateLeft.Path)
                throw new InvalidDataException("新しい左動画のフレームが表示されません。");
            var expectedLeft = await comparison.ReadFrameAsync(alternateLeft.Path, firstFrame, CancellationToken.None);
            if (!currentLeft!.Pixels.AsSpan().SequenceEqual(expectedLeft.Pixels))
                throw new InvalidDataException("新しい左動画の画素が一致しません。");
            LeftSelect.SelectedItem = originalLeft;
            if (selectionChangeTask is not null) await selectionChangeTask;

            operation = "連続選択";
            RightSelect.SelectedItem = original;
            RightSelect.SelectedItem = alternate;
            if (selectionChangeTask is not null) await selectionChangeTask;
            await Task.Delay(150);
            if (CheckAlignedPair() != firstFrame || preparedRightPath != alternate.Path)
                throw new InvalidDataException("古い選択の結果が画面を上書きしました。");

            operation = "再生中の選択変更";
            ViewSelect.SelectedIndex = 1;
            PlayPause_Click(this, new RoutedEventArgs());
            await WaitForFrameAsync(firstFrame + 10);
            var beforeSwitch = CheckAlignedPair();
            RightSelect.SelectedItem = original;
            if (selectionChangeTask is not null) await selectionChangeTask;
            if (playbackCancellation is null || playbackPaused ||
                RightSelect.SelectedItem is not VideoChoice selectedRight ||
                !string.Equals(selectedRight.Path, original.Path, StringComparison.OrdinalIgnoreCase) ||
                CheckAlignedPair() < beforeSwitch || WipeRightImage.Source is null)
                throw new InvalidDataException("動画変更後に同じ位置から再生できません。");
            await WaitForFrameAsync(beforeSwitch + 5);

            operation = "一時停止中の選択変更";
            PlayPause_Click(this, new RoutedEventArgs());
            var pausedFrame = CheckAlignedPair();
            RightSelect.SelectedItem = alternate;
            if (selectionChangeTask is not null) await selectionChangeTask;
            await Task.Delay(150);
            if (playbackCancellation is not null || CheckAlignedPair() != pausedFrame ||
                preparedRightPath != alternate.Path)
                throw new InvalidDataException("一時停止中の切り替え後にフレームが変わりました。");
            ViewSelect.SelectedIndex = 2;
            if (HeatmapImage.Source is null)
                throw new InvalidDataException("切り替え後のヒートマップがありません。");
            WriteVerificationResult("comparison-selection", new
            {
                LeftVideo = Path.GetFileName(leftPath),
                OriginalRight = Path.GetFileName(original.Path),
                AlternateRight = Path.GetFileName(alternate.Path),
                InitialFrame = firstFrame, PlaybackSwitchFrame = beforeSwitch,
                StaticPixelsMatched = true, LeftSelectionPixelsMatched = true,
                OldResultDiscarded = true, PlaybackResumed = true,
                PausedSelectionStable = true, WipeAndHeatmapUpdated = true, Result = "合格"
            });
        }
        catch (Exception exception)
        {
            WriteVerificationResult("comparison-selection-failed", new
            {
                LeftVideo = Path.GetFileName(leftPath), RightVideo = Path.GetFileName(rightPath),
                FailedOperation = operation, ErrorCode = $"0x{exception.HResult:X8}", Result = "失敗"
            });
            Application.Current.Shutdown(1);
        }
        finally { Window.GetWindow(this)?.Close(); }
    }

    private async Task WaitForFrameAsync(long target)
    {
        for (var attempt = 0; attempt < 400; attempt++)
        {
            if (currentLeft?.FrameNumber >= target && currentRight?.FrameNumber == currentLeft.FrameNumber)
                return;
            await Task.Delay(25);
        }
        throw new TimeoutException($"フレーム #{target} に進みませんでした。");
    }

    private long CheckAlignedPair()
    {
        if (currentLeft is null || currentRight is null ||
            currentLeft.FrameNumber != currentRight.FrameNumber)
            throw new InvalidOperationException("左右の元フレーム番号が一致しません。");
        return currentLeft.FrameNumber;
    }

    private void CheckDisplayedHeatmap(int blurRadius)
    {
        if (currentLeft is null || currentRight is null || HeatmapImage.Source is not BitmapSource bitmap)
            throw new InvalidOperationException("ヒートマップが表示されていません。");
        var expected = FrameComparisonService.CreateHeatmap(currentLeft, currentRight,
            SelectedGain(), blurRadius);
        var actual = new byte[expected.Heatmap.Length];
        bitmap.CopyPixels(actual, expected.Width * 4, 0);
        if (!actual.AsSpan().SequenceEqual(expected.Heatmap))
            throw new InvalidOperationException($"ぼかし半径{blurRadius}の表示画素が一致しません。");
    }

    private static void WriteVerificationResult(string prefix, object report)
    {
        var workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var directory = Path.Combine(workspace, "measurement-results");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"{prefix}-{Guid.NewGuid():N}.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void GainSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        heatmapGain = SelectedGain();
        if ((playbackCancellation is null || playbackPaused) && currentLeft is not null && currentRight is not null)
            RefreshHeatmap();
    }

    private void BlurSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        heatmapBlurRadius = SelectedBlurRadius();
        if ((playbackCancellation is null || playbackPaused) && currentLeft is not null && currentRight is not null)
            RefreshHeatmap();
    }

    private int SelectedBlurRadius() => BlurSelect.SelectedItem is ComboBoxItem { Tag: string text } &&
        int.TryParse(text, out var value) ? value : 0;

    private void ViewSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != ViewSelect || ThreeViewGrid is null || WipeViewGrid is null ||
            HeatmapContent is null || TwoScreenContent is null) return;
        var wipe = ViewSelect.SelectedIndex == 1;
        wipeMode = wipe;
        twoScreenMode = ViewSelect.SelectedIndex == 0;
        if (twoScreenMode && !ReferenceEquals(TwoScreenContent.Content, ThreeViewGrid))
        {
            HeatmapContent.Content = null;
            TwoScreenContent.Content = ThreeViewGrid;
        }
        else if (!twoScreenMode && !ReferenceEquals(HeatmapContent.Content, ThreeViewGrid))
        {
            TwoScreenContent.Content = null;
            HeatmapContent.Content = ThreeViewGrid;
        }
        HeatmapPanel.Visibility = twoScreenMode ? Visibility.Collapsed : Visibility.Visible;
        HeatmapColumn.Width = twoScreenMode ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        if (BlurSelect is not null) BlurSelect.IsEnabled = !wipe;
        ThreeViewGrid.Visibility = wipe ? Visibility.Collapsed : Visibility.Visible;
        WipeViewGrid.Visibility = wipe ? Visibility.Visible : Visibility.Collapsed;
        if (wipe) UpdateWipeBoundary();
        else if (currentLeft is not null && currentRight is not null) RefreshHeatmap();
    }

    private void UpdateWipeBoundary() => wipeBoundaryController.Update();

    private async Task LoadComparisonAsync()
    {
        if (LeftSelect.SelectedItem is not VideoChoice left ||
            RightSelect.SelectedItem is not VideoChoice right ||
            !long.TryParse(FrameNumberBox.Text, out var frameNumber) || frameNumber < 0)
        {
            ResultText.Text = "0以上の元フレーム番号と左右の動画を指定してください。";
            return;
        }
        loadCancellation?.Cancel();
        loadCancellation?.Dispose();
        var previousReader = preparedReader;
        preparedReader = null;
        if (previousReader is not null) _ = previousReader.DisposeAsync();
        loadCancellation = new CancellationTokenSource();
        var token = loadCancellation.Token;
        ResultText.Text = $"フレーム #{frameNumber} を復号中…";
        StreamingFramePairReader? candidate = null;
        try
        {
            ComparisonFrame leftFrame;
            ComparisonFrame rightFrame;
            try
            {
                candidate = await StreamingFramePairReader.OpenAsync(ffmpegPath, ffprobePath,
                    left.Path, right.Path, frameNumber, token);
                var pair = await candidate.ReadNextAsync(token) ??
                    throw new InvalidDataException("指定フレームを取得できませんでした。");
                (leftFrame, rightFrame) = pair;
            }
            catch (InvalidDataException)
            {
                // 再生できない組み合わせでも、従来の静止比較は利用できるようにする。
                if (candidate is not null)
                {
                    await candidate.DisposeAsync();
                    candidate = null;
                }
                var leftTask = comparison.ReadFrameAsync(left.Path, frameNumber, token);
                var rightTask = comparison.ReadFrameAsync(right.Path, frameNumber, token);
                await Task.WhenAll(leftTask, rightTask);
                leftFrame = await leftTask;
                rightFrame = await rightTask;
            }
            if (token.IsCancellationRequested) return;
            currentLeft = leftFrame;
            currentRight = rightFrame;
            playingLeft = playingRight = playingHeatmap = null;
            var leftBitmap = CreateBitmap(leftFrame.Width, leftFrame.Height, leftFrame.Pixels);
            var rightBitmap = CreateBitmap(rightFrame.Width, rightFrame.Height, rightFrame.Pixels);
            LeftImage.Source = WipeLeftImage.Source = leftBitmap;
            RightImage.Source = WipeRightImage.Source = rightBitmap;
            UpdateWipeBoundary();
            RefreshHeatmap();
            FrameShown?.Invoke();
            if (candidate is not null)
            {
                preparedReader = candidate;
                preparedLeftPath = left.Path;
                preparedRightPath = right.Path;
                candidate = null;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (!token.IsCancellationRequested)
                ResultText.Text = $"比較画像を表示できませんでした（0x{exception.HResult:X8}）。";
        }
        finally
        {
            if (candidate is not null) await candidate.DisposeAsync();
        }
    }

    private void RefreshHeatmap()
    {
        if (currentLeft is null || currentRight is null) return;
        var gain = SelectedGain();
        var blurRadius = SelectedBlurRadius();
        var difference = FrameComparisonService.CreateHeatmap(currentLeft, currentRight, gain, blurRadius);
        HeatmapImage.Source = CreateBitmap(difference.Width, difference.Height, difference.Heatmap);
        ResultText.Text = $"元フレーム #{currentLeft.FrameNumber} / " +
            $"{(blurRadius == 0 ? "平均RGB差" : "ぼかし後平均RGB差")} " +
            $"{difference.MeanAbsoluteRgbDifference:F2}（0～255） / 差分{gain}倍表示" +
            $" / ぼかし: {((ComboBoxItem)BlurSelect.SelectedItem).Content}。" +
            "色範囲や圧縮方式の差も含み、品質の優劣を示す値ではありません。";
    }

    private static BitmapSource CreateBitmap(int width, int height, byte[] pixels)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32,
            null, pixels, checked(width * 4));
        bitmap.Freeze();
        return bitmap;
    }

    public void Dispose()
    {
        ++selectionGeneration;
        resumeRequested = false;
        CompositionTarget.Rendering -= ObserveComposition;
        StopPlayback();
        loadCancellation?.Cancel();
        loadCancellation?.Dispose();
        if (preparedReader is not null) _ = preparedReader.DisposeAsync();
    }

    private sealed record VideoChoice(string Name, string Path);
}
