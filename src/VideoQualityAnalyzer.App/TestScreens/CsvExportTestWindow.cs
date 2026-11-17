using System.IO;
using System.Windows;
using System.Windows.Controls;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

// CSVの生成・保存を主画面とは独立して確認する。
public sealed class CsvExportTestWindow : Window
{
    private readonly VideoDescriptor reference = new("reference", "元動画.mkv", 1920, 1080,
        "ffv1", 30, 30, TimeSpan.FromSeconds(2), false, 60, 1);
    private readonly QualityCsvItem[] items;
    private readonly Dictionary<(string VideoId, QualityMetric Metric), QualityResult> results = [];
    private readonly TextBlock status = new() { Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap };

    public CsvExportTestWindow(bool automatic)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "品質CSV出力検証";
        Width = 480;
        Height = 170;
        var first = new VideoDescriptor("first", "=設定,\"A\".mkv", 1920, 1080,
            "h265", 30, 30, TimeSpan.FromSeconds(2), false, 60, 1, 2_450_500, BitrateSource.VideoStream);
        var second = first with { Id = "second", Path = "encode_B.mkv", AverageBitrate = 1_500_000,
            BitrateSource = BitrateSource.Container };
        items = [new(first, "設定A"), new(second, "設定B")];
        results[(first.Id, QualityMetric.Ssim)] = new(QualityMetric.Ssim, 0.978,
            [new QualityFrame(0, 0.982), new QualityFrame(1, 0.974)]);
        results[(second.Id, QualityMetric.Psnr)] = new(QualityMetric.Psnr, 36,
            [new QualityFrame(0, 35), new QualityFrame(1, 37)]);

        var panel = new StackPanel { Margin = new Thickness(12) };
        Content = panel;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(buttons);
        foreach (var frames in new[] { true, false })
        {
            var button = new Button { Content = frames ? "フレームCSVを保存" : "ファイルCSVを保存", Margin = new Thickness(4) };
            button.Click += async (_, _) =>
            {
                var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "CSVファイル (*.csv)|*.csv",
                    FileName = frames ? "frames.csv" : "files.csv" };
                if (dialog.ShowDialog(this) != true) return;
                await SaveAsync(dialog.FileName, frames);
            };
            buttons.Children.Add(button);
        }
        panel.Children.Add(status);
        if (automatic) Loaded += async (_, _) => await VerifyAsync();
    }

    private async Task SaveAsync(string path, bool frames)
    {
        try
        {
            await QualityCsvExporter.SaveAsync(path, frames, reference, items, results);
            status.Text = "CSVを保存しました。";
        }
        catch (Exception exception) { status.Text = "CSVを保存できません: " + exception.Message; }
    }

    private async Task VerifyAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "VideoQualityAnalyzerCsvTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var savedAt = new DateTime(2026, 11, 15, 9, 8, 7);
            var frameDialog = QualityCsvSaveDialog.Create(savedAt, reference, true, items, results);
            var fileDialog = QualityCsvSaveDialog.Create(savedAt, reference, false, items, results);
            if (frameDialog.InitialDirectory != Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) ||
                frameDialog.FileName != "20261115-090807_元動画.mkv_フレームグラフ_SSIM-PSNR.csv" ||
                fileDialog.FileName != "20261115-090807_元動画.mkv_ファイルグラフ_SSIM-PSNR.csv")
                throw new InvalidDataException("保存先または既定ファイル名が不正です。");
            var framesPath = Path.Combine(directory, "frames.csv");
            var filesPath = Path.Combine(directory, "files.csv");
            await SaveAsync(framesPath, true);
            await SaveAsync(filesPath, false);
            var frameBytes = await File.ReadAllBytesAsync(framesPath);
            var fileBytes = await File.ReadAllBytesAsync(filesPath);
            if (!frameBytes.AsSpan(0, 3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }) ||
                !fileBytes.AsSpan(0, 3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }))
                throw new InvalidDataException("UTF-8 BOMがありません。");
            var frames = await File.ReadAllTextAsync(framesPath);
            var files = await File.ReadAllTextAsync(filesPath);
            if (!frames.Contains("SSIM,1,0.033333333333333333,", StringComparison.Ordinal) ||
                !files.Contains("2450.5,\"映像ストリーム\"", StringComparison.Ordinal) ||
                !files.Contains("\"'=設定,\"\"A\"\".mkv\"", StringComparison.Ordinal))
                throw new InvalidDataException("出力値または文字列のエスケープが不正です。");
            Console.WriteLine("品質CSV専用画面: BOM・数値・文字列・保存と読み戻しが合格");
            Application.Current.Shutdown(0);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            Application.Current.Shutdown(1);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
