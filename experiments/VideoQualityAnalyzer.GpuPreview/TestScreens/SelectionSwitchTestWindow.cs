using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.GpuPreview;

// 比較画面へ組み込む前に、選択変更と旧読込結果の破棄を確かめる専用画面。
public sealed class SelectionSwitchTestWindow : Window
{
    private readonly string leftPath;
    private readonly string firstRightPath;
    private readonly string secondRightPath;
    private readonly long frameNumber;
    private readonly ComboBox rightSelect = new() { Width = 420 };
    private readonly TextBlock status = new() { Margin = new Thickness(8) };
    private readonly Image leftImage = new() { Stretch = Stretch.Uniform };
    private readonly Image rightImage = new() { Stretch = Stretch.Uniform };
    private CancellationTokenSource? loadCancellation;
    private Task? currentLoad;
    private ComparisonFrame? currentRight;

    public SelectionSwitchTestWindow(string leftPath, string firstRightPath,
        string secondRightPath, long frameNumber)
    {
        this.leftPath = leftPath;
        this.firstRightPath = firstRightPath;
        this.secondRightPath = secondRightPath;
        this.frameNumber = frameNumber;
        Title = "動画選択の切り替え検証";
        Width = 1000;
        Height = 560;
        var root = new DockPanel();
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        controls.Children.Add(new TextBlock { Text = "右動画", VerticalAlignment = VerticalAlignment.Center });
        controls.Children.Add(rightSelect);
        DockPanel.SetDock(controls, Dock.Top);
        root.Children.Add(controls);
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status);
        var images = new Grid();
        images.ColumnDefinitions.Add(new ColumnDefinition());
        images.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(rightImage, 1);
        images.Children.Add(leftImage);
        images.Children.Add(rightImage);
        root.Children.Add(images);
        Content = root;
        rightSelect.Items.Add(firstRightPath);
        rightSelect.Items.Add(secondRightPath);
        rightSelect.SelectionChanged += (_, _) =>
        {
            if (rightSelect.SelectedItem is string selected)
                currentLoad = LoadPairAsync(selected);
        };
        Loaded += async (_, _) => await VerifyAsync();
        Closed += (_, _) => { loadCancellation?.Cancel(); loadCancellation?.Dispose(); };
    }

    private async Task LoadPairAsync(string rightPath)
    {
        loadCancellation?.Cancel();
        loadCancellation?.Dispose();
        loadCancellation = new CancellationTokenSource();
        var token = loadCancellation.Token;
        status.Text = "選択した動画を読込中…";
        var ffmpeg = FfmpegRgbReader.FindExecutable() ?? throw new FileNotFoundException("FFmpegがありません。");
        var ffprobe = FfprobeService.FindExecutable() ?? throw new FileNotFoundException("FFprobeがありません。");
        await using var reader = await StreamingFramePairReader.OpenAsync(ffmpeg, ffprobe,
            leftPath, rightPath, frameNumber, token);
        var pair = await reader.ReadNextAsync(token) ?? throw new InvalidDataException("フレームがありません。");
        if (token.IsCancellationRequested) return;
        leftImage.Source = ToBitmap(pair.Left);
        rightImage.Source = ToBitmap(pair.Right);
        currentRight = pair.Right;
        status.Text = $"右動画を切り替えました。左右とも元フレーム #{pair.Left.FrameNumber}";
    }

    private async Task VerifyAsync()
    {
        try
        {
            rightSelect.SelectedIndex = 0;
            if (currentLoad is not null) await currentLoad;
            rightSelect.SelectedIndex = 1;
            if (currentLoad is not null) await currentLoad;
            if (currentRight is null || currentRight.FrameNumber != frameNumber ||
                rightSelect.SelectedItem as string != secondRightPath)
                throw new InvalidDataException("選択変更後のフレームが一致しません。");
            var ffmpeg = FfmpegRgbReader.FindExecutable() ?? throw new FileNotFoundException("FFmpegがありません。");
            var ffprobe = FfprobeService.FindExecutable() ?? throw new FileNotFoundException("FFprobeがありません。");
            var expected = await new FrameComparisonService(ffmpeg, ffprobe).ReadFrameAsync(
                secondRightPath, frameNumber, CancellationToken.None);
            if (!currentRight.Pixels.AsSpan().SequenceEqual(expected.Pixels))
                throw new InvalidDataException("切り替え後の画素が一致しません。");
            SaveReport(new { FirstRight = Path.GetFileName(firstRightPath),
                SecondRight = Path.GetFileName(secondRightPath), FrameNumber = frameNumber, Result = "合格" });
            await Task.Delay(300);
            Close();
        }
        catch (Exception exception)
        {
            SaveReport(new { FrameNumber = frameNumber,
                ErrorCode = $"0x{exception.HResult:X8}", Result = "失敗" });
            Application.Current.Shutdown(1);
        }
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
        File.WriteAllText(Path.Combine(directory, $"selection-switch-{Guid.NewGuid():N}.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }
}
