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

public partial class ComparisonWindow : Window
{
    private readonly FrameComparisonService comparison;
    private readonly string ffmpegPath;
    private readonly string ffprobePath;
    private readonly bool benchmarkMode;
    private readonly bool captureBenchmark;
    private readonly bool verifyComparison;
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
    private ComparisonFrame? currentLeft;
    private ComparisonFrame? currentRight;
    private volatile bool playbackPaused;
    private volatile bool wipeMode;
    private volatile int heatmapGain = 16;
    private WriteableBitmap? playingLeft;
    private WriteableBitmap? playingRight;
    private WriteableBitmap? playingHeatmap;

    public ComparisonWindow(string leftPath, string rightPath, long frameNumber,
        bool showWipe = false, bool benchmarkMode = false, bool captureBenchmark = false,
        bool verifyComparison = false)
    {
        InitializeComponent();
        this.leftPath = leftPath;
        this.rightPath = rightPath;
        this.benchmarkMode = benchmarkMode;
        this.captureBenchmark = captureBenchmark;
        this.verifyComparison = verifyComparison;
        if (captureBenchmark)
        {
            Left = 0;
            Top = 0;
            Topmost = true;
        }
        ffmpegPath = FfmpegRgbReader.FindExecutable() ??
            throw new FileNotFoundException("FFmpegが見つかりません。");
        ffprobePath = FfprobeService.FindExecutable() ??
            throw new FileNotFoundException("FFprobeが見つかりません。");
        comparison = new FrameComparisonService(ffmpegPath, ffprobePath);
        var workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var paths = Directory.EnumerateFiles(Path.Combine(workspace, "test-data"))
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
        ViewSelect.SelectedIndex = showWipe ? 1 : 0;
        UpdateWipeBoundary();
        ResultText.Text = "左右に同じ元フレーム番号を指定して比較します。再生中も表示を切り替えられます。";
        Loaded += (_, _) =>
        {
            if (benchmarkMode)
            {
                if (captureBenchmark) PrepareCaptureMarker();
                CompositionTarget.Rendering += ObserveComposition;
                PlayPause_Click(this, new RoutedEventArgs());
            }
            else if (verifyComparison) _ = VerifyComparisonAsync(frameNumber);
            else _ = LoadComparisonAsync();
        };
    }

    private void Compare_Click(object sender, RoutedEventArgs e)
    {
        StopPlayback(keepRequestedFrame: true);
        _ = LoadComparisonAsync();
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
        playbackPaused = false;
        PlayPauseButton.Content = "一時停止";
        var cancellation = new CancellationTokenSource();
        playbackCancellation = cancellation;
        _ = PlayAsync(left.Path, right.Path, frameNumber, cancellation);
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
        CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        ResultText.Text = "連続復号を準備中…";
        try
        {
            await Task.Run(async () =>
            {
            await using var reader = await StreamingFramePairReader.OpenAsync(ffmpegPath, ffprobePath,
                leftPath, rightPath, firstFrameNumber, token);
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
            while (!token.IsCancellationRequested)
            {
                if (playbackPaused)
                {
                    clock.Stop();
                    await Task.Delay(20, token);
                    continue;
                }
                var pair = await reader.ReadNextAsync(token);
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
                var difference = wipeMode ? null : FrameComparisonService.CreateHeatmap(left, right, heatmapGain);
                await Dispatcher.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested) return;
                    currentLeft = left;
                    currentRight = right;
                    UpdatePlayingBitmaps(left, right, difference);
                    if (captureBenchmark) UpdateCaptureMarker(left.FrameNumber);
                    displayed++;
                    if (benchmarkMode && left.FrameNumber >= firstMeasured)
                        measuredSubmitted.Add(left.FrameNumber);
                    if (displayed % 6 == 0)
                    {
                        FrameNumberBox.Text = left.FrameNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        ResultText.Text = $"再生比較 #{left.FrameNumber} / 描画要求 {displayed / Math.Max(0.001, clock.Elapsed.TotalSeconds):F1} fps" +
                            $" / 遅延破棄 {skipped}枚" +
                            (difference is null ? "" : $" / 平均RGB差 {difference.MeanAbsoluteRgbDifference:F2}") +
                            "。実画面提示fpsは未測定。";
                    }
                });
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
            }, token);
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
        if (!IsVisible || WindowState == WindowState.Minimized)
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
        Close();
    }

    private async Task VerifyComparisonAsync(long firstFrame)
    {
        var operation = "開始";
        try
        {
            PlayPause_Click(this, new RoutedEventArgs());
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
            foreach (var percent in new[] { 25d, 75d })
            {
                WipeSlider.Value = percent;
                if (WipeRightLayer.Clip is not RectangleGeometry clip ||
                    Math.Abs(clip.Rect.Left - WipeBoundary.Margin.Left - WipeBoundary.Width / 2) > 0.5)
                    throw new InvalidOperationException("ワイプ境界と切り替え位置が一致しません。");
            }
            ViewSelect.SelectedIndex = 0;

            operation = "再開";
            PlayPause_Click(this, new RoutedEventArgs());
            await WaitForFrameAsync(pausedFrame + 10);
            var resumedFrame = CheckAlignedPair();

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
                WipeBoundaryAligned = true, SeekedPixelsMatchStaticDecode = true,
                PausedAndStoppedFrameStable = true, Result = "合格"
            });
        }
        catch (Exception exception)
        {
            WriteVerificationResult("comparison-operations-failed", new
            {
                LeftVideo = Path.GetFileName(leftPath), RightVideo = Path.GetFileName(rightPath),
                FailedOperation = operation, ErrorType = exception.GetType().Name,
                ErrorCode = $"0x{exception.HResult:X8}", Result = "失敗"
            });
            Application.Current.Shutdown(1);
        }
        finally { Close(); }
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
        if (playbackCancellation is null && currentLeft is not null && currentRight is not null)
            RefreshHeatmap();
    }

    private void ViewSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThreeViewGrid is null || WipeViewGrid is null) return;
        var wipe = ViewSelect.SelectedIndex == 1;
        wipeMode = wipe;
        ThreeViewGrid.Visibility = wipe ? Visibility.Collapsed : Visibility.Visible;
        WipeViewGrid.Visibility = wipe ? Visibility.Visible : Visibility.Collapsed;
        if (wipe) UpdateWipeBoundary();
        else if (currentLeft is not null && currentRight is not null) RefreshHeatmap();
    }

    private void WipeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        UpdateWipeBoundary();

    private void WipeViewport_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateWipeBoundary();

    private void UpdateWipeBoundary()
    {
        if (WipeViewport is null || WipeSlider is null || WipeRightLayer is null ||
            WipeBoundary is null || WipePositionText is null) return;
        var width = WipeViewport.ActualWidth;
        var height = WipeViewport.ActualHeight;
        var fraction = WipeSlider.Value / 100;
        var aspect = currentLeft is null ? 4d / 3 : (double)currentLeft.Width / currentLeft.Height;
        var imageWidth = Math.Min(width, height * aspect);
        var imageLeft = (width - imageWidth) / 2;
        var boundary = imageLeft + imageWidth * fraction;
        WipeRightLayer.Clip = new RectangleGeometry(new Rect(boundary, 0,
            Math.Max(0, width - boundary), height));
        WipeBoundary.Margin = new Thickness(boundary - WipeBoundary.Width / 2, 0, 0, 0);
        WipeBoundary.Visibility = fraction > 0 && fraction < 1 ? Visibility.Visible : Visibility.Collapsed;
        WipePositionText.Text = $"境界 {WipeSlider.Value:F0}%（左側: 左動画 / 右側: 右動画）";
    }

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
        loadCancellation = new CancellationTokenSource();
        var token = loadCancellation.Token;
        ResultText.Text = $"フレーム #{frameNumber} を復号中…";
        try
        {
            var leftTask = comparison.ReadFrameAsync(left.Path, frameNumber, token);
            var rightTask = comparison.ReadFrameAsync(right.Path, frameNumber, token);
            await Task.WhenAll(leftTask, rightTask);
            var leftFrame = await leftTask;
            var rightFrame = await rightTask;
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
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (!token.IsCancellationRequested)
                ResultText.Text = $"比較画像を作成できませんでした（0x{exception.HResult:X8}）。";
        }
    }

    private void RefreshHeatmap()
    {
        if (currentLeft is null || currentRight is null) return;
        var gain = SelectedGain();
        var difference = FrameComparisonService.CreateHeatmap(currentLeft, currentRight, gain);
        HeatmapImage.Source = CreateBitmap(difference.Width, difference.Height, difference.Heatmap);
        ResultText.Text = $"元フレーム #{currentLeft.FrameNumber} / 平均RGB差 {difference.MeanAbsoluteRgbDifference:F2}（0～255）" +
            $" / 差分{gain}倍表示。色範囲や圧縮方式の差も含み、品質の優劣を示す値ではありません。";
    }

    private static BitmapSource CreateBitmap(int width, int height, byte[] pixels)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32,
            null, pixels, checked(width * 4));
        bitmap.Freeze();
        return bitmap;
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= ObserveComposition;
        StopPlayback();
        loadCancellation?.Cancel();
        loadCancellation?.Dispose();
    }

    private sealed record VideoChoice(string Name, string Path);
}
