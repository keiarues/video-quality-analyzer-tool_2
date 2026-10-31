using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.GpuPreview;

// 比較画面へ組み込む前に、指定位置からの復号とReaderの引き継ぎを画面で検証する。
public sealed class SeekHandoffTestWindow : Window
{
    private readonly string leftPath;
    private readonly string rightPath;
    private readonly long firstFrame;
    private readonly bool automatic;
    private readonly TextBlock status = new() { Margin = new Thickness(12), FontSize = 16 };
    private readonly Image leftImage = new() { Stretch = Stretch.Uniform };
    private readonly Image rightImage = new() { Stretch = Stretch.Uniform };
    private readonly Button nextButton = new() { Content = "次のフレームを表示", Margin = new Thickness(12), IsEnabled = false };
    private StreamingFramePairReader? reader;
    private long lastFrame;

    public SeekHandoffTestWindow(string leftPath, string rightPath, long firstFrame, bool automatic)
    {
        this.leftPath = leftPath;
        this.rightPath = rightPath;
        this.firstFrame = firstFrame;
        this.automatic = automatic;
        Title = "シークと再生引き継ぎの検証";
        Width = 1100;
        Height = 550;
        var root = new DockPanel();
        DockPanel.SetDock(status, Dock.Top);
        root.Children.Add(status);
        DockPanel.SetDock(nextButton, Dock.Bottom);
        root.Children.Add(nextButton);
        var images = new Grid();
        images.ColumnDefinitions.Add(new ColumnDefinition());
        images.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(rightImage, 1);
        images.Children.Add(leftImage);
        images.Children.Add(rightImage);
        root.Children.Add(images);
        Content = root;
        nextButton.Click += async (_, _) =>
        {
            try { await ShowNextAsync(); }
            catch (Exception exception) { status.Text = $"検証失敗: {exception.Message}"; }
        };
        Loaded += async (_, _) => await InitializeAsync();
        Closed += (_, _) => { if (reader is not null) _ = reader.DisposeAsync(); };
    }

    private async Task InitializeAsync()
    {
        try
        {
            status.Text = $"元フレーム #{firstFrame} の近くから復号中…";
            var ffmpeg = FfmpegRgbReader.FindExecutable() ?? throw new FileNotFoundException("FFmpegがありません。");
            var ffprobe = FfprobeService.FindExecutable() ?? throw new FileNotFoundException("FFprobeがありません。");
            var timer = Stopwatch.StartNew();
            reader = await StreamingFramePairReader.OpenAsync(ffmpeg, ffprobe,
                leftPath, rightPath, firstFrame, CancellationToken.None);
            var pair = await reader.ReadNextAsync(CancellationToken.None) ??
                throw new InvalidDataException("最初のフレームを取得できません。");
            var firstMilliseconds = timer.Elapsed.TotalMilliseconds;
            ShowPair(pair.Left, pair.Right);
            nextButton.IsEnabled = true;
            if (!automatic) return;
            await ShowNextAsync();
            var report = new
            {
                LeftVideo = Path.GetFileName(leftPath), RightVideo = Path.GetFileName(rightPath),
                FirstFrame = firstFrame, HandoffFrame = lastFrame,
                FirstFrameMilliseconds = firstMilliseconds,
                HandoffMilliseconds = timer.Elapsed.TotalMilliseconds - firstMilliseconds,
                Result = "合格"
            };
            SaveReport(report);
            await Task.Delay(300);
            Close();
        }
        catch (Exception exception)
        {
            status.Text = $"検証失敗: {exception.Message}";
            if (automatic)
            {
                SaveReport(new { LeftVideo = Path.GetFileName(leftPath), RightVideo = Path.GetFileName(rightPath),
                    FirstFrame = firstFrame, ErrorCode = $"0x{exception.HResult:X8}", Result = "失敗" });
                Application.Current.Shutdown(1);
            }
        }
    }

    private async Task ShowNextAsync()
    {
        if (reader is null) throw new InvalidOperationException("復号器がありません。");
        var pair = await reader.ReadNextAsync(CancellationToken.None) ??
            throw new InvalidDataException("次のフレームを取得できません。");
        if (pair.Left.FrameNumber != lastFrame + 1 ||
            pair.Right.FrameNumber != pair.Left.FrameNumber)
            throw new InvalidDataException("引き継ぎ後の左右フレーム番号が不正です。");
        ShowPair(pair.Left, pair.Right);
    }

    private void ShowPair(ComparisonFrame left, ComparisonFrame right)
    {
        if (left.FrameNumber != right.FrameNumber)
            throw new InvalidDataException("左右フレーム番号が異なります。");
        lastFrame = left.FrameNumber;
        leftImage.Source = ToBitmap(left);
        rightImage.Source = ToBitmap(right);
        status.Text = $"左右とも元フレーム #{lastFrame}。同じ復号器で次のフレームを取得できます。";
    }

    private static BitmapSource ToBitmap(ComparisonFrame frame)
    {
        var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96,
            PixelFormats.Bgra32, null, frame.Pixels, frame.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static void SaveReport(object report)
    {
        var workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var directory = Path.Combine(workspace, "measurement-results");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"seek-handoff-{Guid.NewGuid():N}.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }
}
