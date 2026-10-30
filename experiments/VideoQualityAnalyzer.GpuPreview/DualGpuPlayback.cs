using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Channels;
using System.Windows.Threading;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.GpuPreview;

// 左右のReaderを独立させ、選択変更とシークでは古い世代の結果を破棄する。
internal sealed class DualGpuPlayback : IDisposable
{
    private static readonly ConcurrentDictionary<string, TimeSpan> videoStartPtsCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly GpuVideoHost leftHost;
    private readonly GpuVideoHost rightHost;
    private readonly Stopwatch clock = new();
    private readonly DispatcherTimer videoTimer = new() { Interval = TimeSpan.FromMilliseconds(10) };
    private Slot? left;
    private Slot? right;
    private TimeSpan basePosition;
    private string? failure;
    private bool finished;
    private bool paused;
    private bool stopped;
    private bool disposed;
    private readonly Guid runId = Guid.NewGuid();
    private string? summaryState;

    public DualGpuPlayback(GpuVideoHost leftHost, GpuVideoHost rightHost)
    {
        this.leftHost = leftHost;
        this.rightHost = rightHost;
        videoTimer.Tick += (_, _) => PresentDueFrames();
    }

    public TimeSpan Position => basePosition + clock.Elapsed;
    public bool IsPaused => paused || stopped;
    public FramePair CaptureFramePair() => new(left?.Submitted > 0, right?.Submitted > 0,
        left?.LastPts.Ticks ?? 0, right?.LastPts.Ticks ?? 0,
        left?.StartPts.Ticks ?? 0, right?.StartPts.Ticks ?? 0,
        leftHost.LastPresentedSourcePts?.Ticks, rightHost.LastPresentedSourcePts?.Ticks);
    public string Status => failure is not null ? $"GPU同時表示に失敗: {failure}" :
        (stopped ? "停止中 / " : paused ? "一時停止中 / " : "再生中 / ") +
        $"共通時計 {Position.TotalSeconds:F2}秒 / 左描画要求: {left?.Submitted ?? 0}枚 PTS {left?.LastPts.TotalSeconds ?? 0:F2}秒 " +
        $"(破棄 {left?.Dropped ?? 0}) / 右描画要求: {right?.Submitted ?? 0}枚 PTS {right?.LastPts.TotalSeconds ?? 0:F2}秒 " +
        $"(破棄 {right?.Dropped ?? 0}) / 要求PTS差 {((left?.LastPts ?? TimeSpan.Zero) - (right?.LastPts ?? TimeSpan.Zero)).Duration().TotalMilliseconds:F0}ms" +
        (right?.SwitchMs is { } switchMs ? $" / 右切替後の初回描画要求 {switchMs:F0}ms" : "") +
        (finished ? " / 読み取り終了" : "") +
        (summaryState is null ? "" : $" / {summaryState}") +
        (leftHost.StatisticsEnabled ? $" / 左 {leftHost.PresentationStatus} / 右 {rightHost.PresentationStatus}" : "") +
        (leftHost.StatisticsEnabled ? "（DXGI観測値。外部照合・性能判定は未実施）" : "（実提示fpsは未測定）");

    public void Start(string leftPath, string rightPath)
    {
        left = BeginSlot(leftPath, leftHost, TimeSpan.Zero);
        right = BeginSlot(rightPath, rightHost, TimeSpan.Zero);
        _ = StartWhenReady(left, right, "開始");
    }

    public void SaveWindowMetricsIfReady()
    {
        if (summaryState is not null || !leftHost.StatisticsEnabled || left is null || right is null) return;
        var leftMetrics = leftHost.WindowMetrics;
        var rightMetrics = rightHost.WindowMetrics;
        if (!leftMetrics.Complete || !rightMetrics.Complete) return;
        try
        {
            var workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
            var directory = Path.Combine(workspace, "measurement-results");
            Directory.CreateDirectory(directory);
            var name = $"gpu-{runId:N}.json";
            var report = new
            {
                SchemaVersion = 1,
                RunId = runId,
                RecordedAt = DateTimeOffset.Now,
                WindowStartPtsSeconds = 2,
                WindowEndPtsSeconds = 32,
                Method = "DXGI提示IDとソースPTSの対応。各フレームの厳密な提示時刻は未記録。",
                PresentSyncInterval = 0,
                VisibilityVerified = false,
                LeftVideo = Path.GetFileName(left.Path),
                RightVideo = Path.GetFileName(right.Path),
                Left = leftMetrics,
                Right = rightMetrics,
                LeftDroppedSourcePtsTicks = left.DroppedSourcePtsTicks,
                RightDroppedSourcePtsTicks = right.DroppedSourcePtsTicks
            };
            File.WriteAllText(Path.Combine(directory, name),
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            summaryState = $"30秒集計保存 {name}";
        }
        catch (Exception exception)
        {
            summaryState = $"集計保存失敗 0x{exception.HResult:X8}";
        }
    }

    public void Seek(TimeSpan position)
        => SeekInternal(position, strict: false);

    public void TogglePause()
    {
        if (disposed || left is null || right is null) return;
        if (paused || stopped)
        {
            paused = false;
            stopped = false;
            SeekInternal(left.LastPts, strict: false);
        }
        else
        {
            paused = true;
            SeekInternal(left.LastPts, strict: false);
        }
    }

    public void Step()
    {
        if (disposed || left is null || right is null) return;
        var target = left.LastPts + TimeSpan.FromMilliseconds(5);
        paused = true;
        stopped = false;
        SeekInternal(target, strict: true);
    }

    public void Stop()
    {
        if (disposed || left is null || right is null) return;
        paused = false;
        stopped = true;
        SeekInternal(TimeSpan.Zero, strict: false);
    }

    private void SeekInternal(TimeSpan position, bool strict)
    {
        if (disposed || left is null || right is null) return;
        position = TimeSpan.FromSeconds(Math.Max(0, position.TotalSeconds));
        clock.Stop();
        videoTimer.Stop();
        basePosition = position;
        clock.Reset();
        finished = false;
        failure = null;
        var oldLeft = left;
        var oldRight = right;
        StopSlot(oldLeft);
        StopSlot(oldRight);
        leftHost.ResetInput();
        rightHost.ResetInput();
        left = BeginSlot(oldLeft.Path, leftHost, position, strict);
        right = BeginSlot(oldRight.Path, rightHost, position, strict);
        _ = StartWhenReady(left, right, "シーク", paused || stopped);
    }

    public void SwitchRight(string path)
    {
        var currentLeft = left;
        if (disposed || currentLeft is null || right is null ||
            string.Equals(right.Path, path, StringComparison.OrdinalIgnoreCase)) return;
        var stayPaused = finished || paused || stopped;
        var oldRight = right;
        StopSlot(oldRight);
        rightHost.ResetInput();
        right = BeginSlot(path, rightHost, Position);
        var currentRight = right;
        currentRight.SwitchStarted = Stopwatch.GetTimestamp();
        finished = stayPaused;
        failure = null;
        _ = StartWhenReady(currentLeft, currentRight, "右動画の切り替え", stayPaused);
    }

    private async Task StartWhenReady(Slot expectedLeft, Slot expectedRight, string operation, bool stayPaused = false)
    {
        try
        {
            await Task.WhenAll(expectedLeft.FirstFrame.Task, expectedRight.FirstFrame.Task);
            if (disposed || !ReferenceEquals(left, expectedLeft) || !ReferenceEquals(right, expectedRight)) return;
            var firstCommonPts = expectedLeft.FirstQueuedPts > expectedRight.FirstQueuedPts ?
                expectedLeft.FirstQueuedPts : expectedRight.FirstQueuedPts;
            if (Position + TimeSpan.FromMilliseconds(17) < firstCommonPts)
            {
                SeekInternal(firstCommonPts, strict: false);
                return;
            }
            if (stayPaused || finished || paused || stopped)
            {
                PresentSlot(expectedLeft, forceFirst: true);
                PresentSlot(expectedRight, forceFirst: true);
                StopSlot(expectedLeft);
                StopSlot(expectedRight);
                return;
            }
            if (!clock.IsRunning) clock.Start();
            videoTimer.Start();
        }
        catch (Exception exception)
        {
            if (!disposed && ReferenceEquals(left, expectedLeft) && ReferenceEquals(right, expectedRight))
                failure = $"{operation}できません（0x{exception.HResult:X8}）";
        }
    }

    private static Slot BeginSlot(string path, GpuVideoHost host, TimeSpan target, bool strict = false)
    {
        var slot = new Slot(path, host);
        slot.Worker = Task.Run(() => ReadFrames(slot, target, strict));
        return slot;
    }

    private static void ReadFrames(Slot slot, TimeSpan target, bool strict)
    {
        var token = slot.Cancellation.Token;
        try
        {
            var probePath = FfprobeService.FindExecutable() ??
                throw new FileNotFoundException("FFprobeが見つかりません。");
            var videoStartPts = videoStartPtsCache.GetOrAdd(slot.Path, path =>
                new FfprobeService(probePath).ReadVideoStartTimeAsync(path, token).GetAwaiter().GetResult());
            using var reader = new GpuSurfaceReader(slot.Path, slot.Host.Device);
            using var origin = reader.ReadNext() ?? throw new InvalidDataException("映像フレームがありません。");
            // Readerの初回サンプルが素材の開始フレームとは限らない。
            slot.StartPts = videoStartPts;
            if (target > TimeSpan.Zero)
                reader.Seek(slot.StartPts + target);
            var count = 0;
            if (target == TimeSpan.Zero)
            {
                // 元フレームは所有権を移す必要があるので、開始位置から改めて読む。
                reader.Seek(slot.StartPts);
            }
            var retriedEarlier = false;
            while (!token.IsCancellationRequested)
            {
                GpuFrame? frame;
                try { frame = reader.ReadNext(); }
                catch (InvalidDataException) when (count == 0 && target > TimeSpan.Zero && !retriedEarlier)
                {
                    reader.Seek(slot.StartPts + TimeSpan.FromSeconds(Math.Max(0, target.TotalSeconds - 2)));
                    retriedEarlier = true;
                    continue;
                }
                if (frame is null && count == 0 && target > TimeSpan.Zero && !retriedEarlier)
                {
                    reader.Seek(slot.StartPts + TimeSpan.FromSeconds(Math.Max(0, target.TotalSeconds - 2)));
                    retriedEarlier = true;
                    continue;
                }
                if (frame is null) break;
                if (frame.Position - slot.StartPts < (strict ? target : target - TimeSpan.FromMilliseconds(17)))
                {
                    frame.Dispose();
                    continue;
                }
                try
                {
                    slot.Frames.Writer.WriteAsync(frame, token).AsTask().GetAwaiter().GetResult();
                    if (count == 0) slot.FirstQueuedPts = frame.Position - slot.StartPts;
                    slot.FirstFrame.TrySetResult();
                    count++;
                }
                catch { frame.Dispose(); throw; }
            }
            if (!slot.FirstFrame.Task.IsCompleted)
                slot.FirstFrame.TrySetException(new InvalidDataException("対象時刻の映像フレームがありません。"));
        }
        catch (OperationCanceledException) { slot.FirstFrame.TrySetCanceled(); }
        catch (Exception exception)
        {
            slot.FirstFrame.TrySetException(exception);
            slot.Error = $"0x{exception.HResult:X8} {exception.GetType().Name}";
        }
        finally { slot.Frames.Writer.TryComplete(); }
    }

    private void PresentDueFrames()
    {
        if (left is null || right is null) return;
        try
        {
            PresentSlot(left);
            PresentSlot(right);
            if (left.Error is not null || right.Error is not null)
            {
                failure = $"左 {left.Error ?? "正常"} / 右 {right.Error ?? "正常"}";
                videoTimer.Stop();
                clock.Stop();
            }
            else if (IsDrained(left))
            {
                finished = true;
                videoTimer.Stop();
                clock.Stop();
                if (right.FirstFrame.Task.IsCompletedSuccessfully)
                {
                    PresentSlot(right, forceFirst: true);
                    StopSlot(right);
                }
            }
        }
        catch (Exception exception)
        {
            failure = $"描画 0x{exception.HResult:X8} {exception.GetType().Name}";
            videoTimer.Stop();
        }
    }

    private void PresentSlot(Slot slot, bool forceFirst = false)
    {
        if (slot.Pending is null && slot.Frames.Reader.TryRead(out var first)) slot.Pending = first;
        GpuFrame? latest = null;
        while (slot.Pending is { } frame && frame.Position - slot.StartPts <= Position)
        {
            if (latest is not null)
            {
                var droppedPts = latest.Position - slot.StartPts;
                if (droppedPts >= TimeSpan.FromSeconds(2) && droppedPts < TimeSpan.FromSeconds(32))
                    slot.DroppedSourcePtsTicks.Add(droppedPts.Ticks);
                latest.Dispose();
                slot.Dropped++;
            }
            latest = frame;
            slot.Pending = slot.Frames.Reader.TryRead(out var next) ? next : null;
        }
        if (forceFirst && latest is null && slot.Pending is { } pending)
        {
            latest = pending;
            slot.Pending = null;
        }
        if (latest is null) return;
        using (latest)
        {
            slot.Host.Render(latest, synchronize: false, sourcePts: latest.Position - slot.StartPts);
            slot.LastPts = latest.Position - slot.StartPts;
            slot.Submitted++;
            if (slot.SwitchStarted != 0)
            {
                slot.SwitchMs = Stopwatch.GetElapsedTime(slot.SwitchStarted).TotalMilliseconds;
                slot.SwitchStarted = 0;
            }
        }
    }

    private static bool IsDrained(Slot slot) => slot.Frames.Reader.Completion.IsCompleted &&
        slot.Pending is null && !slot.Frames.Reader.TryPeek(out _);

    private static void StopSlot(Slot? slot)
    {
        if (slot is null || slot.Stopped) return;
        slot.Stopped = true;
        slot.Cancellation.Cancel();
        slot.Frames.Writer.TryComplete();
        slot.Pending?.Dispose();
        slot.Pending = null;
        while (slot.Frames.Reader.TryRead(out var frame)) frame.Dispose();
        _ = slot.Worker.ContinueWith(_ => slot.Cancellation.Dispose());
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        videoTimer.Stop();
        StopSlot(left);
        StopSlot(right);
    }

    private sealed class Slot(string path, GpuVideoHost host)
    {
        public string Path { get; } = path;
        public GpuVideoHost Host { get; } = host;
        public CancellationTokenSource Cancellation { get; } = new();
        public Task Worker { get; set; } = Task.CompletedTask;
        public Channel<GpuFrame> Frames { get; } = Channel.CreateBounded<GpuFrame>(new BoundedChannelOptions(4)
        {
            SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait
        });
        public TaskCompletionSource FirstFrame { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public GpuFrame? Pending;
        public int Submitted;
        public int Dropped;
        public List<long> DroppedSourcePtsTicks { get; } = new();
        public TimeSpan LastPts;
        public TimeSpan StartPts;
        public TimeSpan FirstQueuedPts;
        public string? Error;
        public bool Stopped;
        public long SwitchStarted;
        public double? SwitchMs;
    }
}

internal sealed record FramePair(bool LeftReady, bool RightReady, long LeftPtsTicks, long RightPtsTicks,
    long LeftFirstPtsTicks, long RightFirstPtsTicks, long? LeftPresentedPtsTicks, long? RightPresentedPtsTicks);
