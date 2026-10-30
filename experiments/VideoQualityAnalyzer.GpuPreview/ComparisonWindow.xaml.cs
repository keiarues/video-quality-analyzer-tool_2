using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.GpuPreview;

public partial class ComparisonWindow : Window
{
    private readonly FrameComparisonService comparison;
    private CancellationTokenSource? loadCancellation;
    private ComparisonFrame? currentLeft;
    private ComparisonFrame? currentRight;

    public ComparisonWindow(string leftPath, string rightPath, long frameNumber)
    {
        InitializeComponent();
        var ffmpegPath = FfmpegRgbReader.FindExecutable() ??
            throw new FileNotFoundException("FFmpegが見つかりません。");
        var ffprobePath = FfprobeService.FindExecutable() ??
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
        ResultText.Text = "再生は停止したままです。左右に同じ元フレーム番号を指定して比較します。";
        Loaded += (_, _) => _ = LoadComparisonAsync();
    }

    private void Compare_Click(object sender, RoutedEventArgs e) => _ = LoadComparisonAsync();

    private void GainSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (currentLeft is not null && currentRight is not null)
            RefreshHeatmap();
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
            LeftImage.Source = CreateBitmap(leftFrame.Width, leftFrame.Height, leftFrame.Pixels);
            RightImage.Source = CreateBitmap(rightFrame.Width, rightFrame.Height, rightFrame.Pixels);
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
        var gain = GainSelect.SelectedItem is ComboBoxItem { Tag: string text } && int.TryParse(text, out var value)
            ? value : 16;
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
        loadCancellation?.Cancel();
        loadCancellation?.Dispose();
    }

    private sealed record VideoChoice(string Name, string Path);
}
