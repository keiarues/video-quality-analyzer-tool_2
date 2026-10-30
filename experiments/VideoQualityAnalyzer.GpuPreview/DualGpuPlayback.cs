using System.Diagnostics;
using System.IO;
using System.Threading.Channels;
using System.Windows.Threading;

namespace VideoQualityAnalyzer.GpuPreview;

// 左右のReaderを独立させ、選択変更とシークでは古い世代の結果を破棄する。
internal sealed class DualGpuPlayback : IDisposable
{
    private readonly GpuVideoHost leftHost;
    private readonly GpuVideoHost rightHost;
    private readonly Stopwatch clock = new();
    private readonly DispatcherTimer videoTimer = new() { Interval = TimeSpan.FromMilliseconds(10) };
    private Slot? left;
    private Slot? right;
    private TimeSpan basePosition;
    private string? failure;
    private bool finished;
    private bool disposed;

    public DualGpuPlayback(GpuVideoHost leftHost, GpuVideoHost rightHost)
    {
        this.leftHost = leftHost;
        this.rightHost = rightHost;
        videoTimer.Tick += (_, _) => PresentDueFrames();
    }

    public TimeSpan Position => basePosition + clock.Elapsed;
    public string Status => failure is not null ? $"GPU同時表示に失敗: {failure}" :
        $"共通時計 {Position.TotalSeconds:F2}秒 / 左描画要求: {left?.Submitted ?? 0}枚 PTS {left?.LastPts.TotalSeconds ?? 0:F2}秒 " +
        $"(破棄 {left?.Dropped ?? 0}) / 右描画要求: {right?.Submitted ?? 0}枚 PTS {right?.LastPts.TotalSeconds ?? 0:F2}秒 " +
        $"(破棄 {right?.Dropped ?? 0}) / 要求PTS差 {((left?.LastPts ?? TimeSpan.Zero) - (right?.LastPts ?? TimeSpan.Zero)).Duration().TotalMilliseconds:F0}ms" +
        (right?.SwitchMs is { } switchMs ? $" / 右切替後の初回描画要求 {switchMs:F0}ms" : "") +
        (finished ? " / 読み取り終了" : "") +
        (leftHost.StatisticsEnabled ? $" / 左 {leftHost.PresentationStatus} / 右 {rightHost.PresentationStatus}" : "") +
        (leftHost.StatisticsEnabled ? "（DXGI観測値。外部照合・性能判定は未実施）" : "（実提示fpsは未測定）");

    public void Start(string leftPath, string rightPath)
    {
        left = BeginSlot(leftPath, leftHost, TimeSpan.Zero);
        right = BeginSlot(rightPath, rightHost, TimeSpan.Zero);
        _ = StartWhenReady(left, right, "開始");
    }

    public void Seek(TimeSpan position)
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
        left = BeginSlot(oldLeft.Path, leftHost, position);
        right = BeginSlot(oldRight.Path, rightHost, position);
        _ = StartWhenReady(left, right, "シーク");
    }

    public void SwitchRight(string path)
    {
        var currentLeft = left;
        if (disposed || currentLeft is null || right is null ||
            string.Equals(right.Path, path, StringComparison.OrdinalIgnoreCase)) return;
        var stayPaused = finished;
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
            if (stayPaused || finished)
            {
                PresentSlot(expectedRight, forceFirst: true);
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

    private static Slot BeginSlot(string path, GpuVideoHost host, TimeSpan target)
    {
        var slot = new Slot(path, host);
        slot.Worker = Task.Run(() => ReadFrames(slot, target));
        return slot;
    }

    private static void ReadFrames(Slot slot, TimeSpan target)
    {
        var token = slot.Cancellation.Token;
        try
        {
            using var reader = new GpuSurfaceReader(slot.Path, slot.Host.Device);
            using var origin = reader.ReadNext() ?? throw new InvalidDataException("映像フレームがありません。");
            slot.StartPts = origin.Position;
            if (target > TimeSpan.Zero)
                reader.Seek(slot.StartPts + target);
            var count = 0;
            if (target == TimeSpan.Zero)
            {
                // 元フレームは所有権を移す必要があるので、開始位置から改めて読む。
                reader.Seek(slot.StartPts);
            }
            var retriedEarlier = false;
            while (count < 900 && !token.IsCancellationRequested)
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
                if (frame.Position - slot.StartPts < target - TimeSpan.FromMilliseconds(17))
                {
                    frame.Dispose();
                    continue;
                }
                try
                {
                    slot.Frames.Writer.WriteAsync(frame, token).AsTask().GetAwaiter().GetResult();
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
            if (latest is not null) { latest.Dispose(); slot.Dropped++; }
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
            slot.Host.Render(latest, sourcePts: latest.Position - slot.StartPts);
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
        public TimeSpan LastPts;
        public TimeSpan StartPts;
        public string? Error;
        public bool Stopped;
        public long SwitchStarted;
        public double? SwitchMs;
    }
}
