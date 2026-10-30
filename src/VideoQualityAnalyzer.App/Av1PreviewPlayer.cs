using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

// 全形式を同じFFmpeg RGB変換とWPF画像表示に通す暫定経路。
internal sealed class Av1PreviewPlayer(Image image) : IDisposable
{
    private readonly object clockLock = new();
    private readonly Stopwatch clock = new();
    private CancellationTokenSource? cancellation;
    private WriteableBitmap? bitmap;
    private VideoDescriptor? video;
    private TimeSpan basePosition;
    private long generation;
    private bool playing;

    public Action<TimeSpan>? FrameShown { get; set; }
    public Action<Exception>? Failed { get; set; }
    public bool IsOpen => video is not null;

    public TimeSpan Position
    {
        get
        {
            lock (clockLock)
                return basePosition + (playing ? clock.Elapsed : TimeSpan.Zero);
        }
    }

    public void Open(VideoDescriptor descriptor, TimeSpan position, bool startPlaying)
    {
        Close();
        video = descriptor;
        bitmap = new WriteableBitmap(descriptor.Width, descriptor.Height,
            96 / descriptor.PixelAspectRatio, 96, PixelFormats.Bgr32, null);
        image.Source = bitmap;
        image.Visibility = System.Windows.Visibility.Visible;
        Restart(position, startPlaying);
    }

    public void Play(TimeSpan position) => Restart(position, true);

    public void Pause()
    {
        lock (clockLock)
        {
            if (!playing) return;
            basePosition += clock.Elapsed;
            clock.Reset();
            playing = false;
        }
    }

    public void Seek(TimeSpan position) => Restart(position, playing);

    private void Restart(TimeSpan position, bool startPlaying)
    {
        var descriptor = video;
        if (descriptor is null) return;
        lock (clockLock)
        {
            basePosition = position;
            playing = startPlaying;
            clock.Reset();
            if (playing) clock.Start();
        }
        cancellation?.Cancel();
        cancellation?.Dispose();
        var source = new CancellationTokenSource();
        cancellation = source;
        var currentGeneration = ++generation;
        _ = Task.Run(() => DecodeLoop(descriptor, position, currentGeneration, source.Token));
    }

    private async Task DecodeLoop(VideoDescriptor descriptor, TimeSpan start, long currentGeneration, CancellationToken token)
    {
        try
        {
            using var reader = new FfmpegRgbReader(descriptor.Path, descriptor.Width, descriptor.Height,
                descriptor.AverageFrameRate, start);
            var shown = false;
            while (!token.IsCancellationRequested)
            {
                if (shown && !IsPlaying())
                {
                    await Task.Delay(20, token);
                    continue;
                }
                var frame = reader.ReadNext();
                if (frame is null) break;
                if (frame.Position < start - TimeSpan.FromMilliseconds(17)) continue;
                while (IsPlaying() && frame.Position > Position + TimeSpan.FromMilliseconds(5))
                {
                    var wait = Math.Clamp((frame.Position - Position).TotalMilliseconds, 1, 20);
                    await Task.Delay(TimeSpan.FromMilliseconds(wait), token);
                }
                if (shown && IsPlaying() && Position - frame.Position > TimeSpan.FromMilliseconds(150))
                    continue;
                if (token.IsCancellationRequested || currentGeneration != generation) break;
                await image.Dispatcher.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested || currentGeneration != generation || bitmap is null) return;
                    bitmap.WritePixels(new System.Windows.Int32Rect(0, 0, descriptor.Width, descriptor.Height),
                        frame.Pixels, descriptor.Width * 4, 0);
                    FrameShown?.Invoke(frame.Position);
                });
                shown = true;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (!token.IsCancellationRequested && currentGeneration == generation)
                await image.Dispatcher.InvokeAsync(() => Failed?.Invoke(exception));
        }
    }

    private bool IsPlaying()
    {
        lock (clockLock) return playing;
    }

    public void Close()
    {
        generation++;
        cancellation?.Cancel();
        cancellation?.Dispose();
        cancellation = null;
        video = null;
        lock (clockLock)
        {
            playing = false;
            basePosition = TimeSpan.Zero;
            clock.Reset();
        }
        image.Source = null;
        image.Visibility = System.Windows.Visibility.Collapsed;
        bitmap = null;
    }

    public void Dispose() => Close();
}
