using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.GpuPreview;

// 比較画面へ組み込む前に、4段階の差分表示と端の画素計算を検証する専用画面。
public sealed class HeatmapBlurTestWindow : Window
{
    public HeatmapBlurTestWindow()
    {
        Title = "ヒートマップぼかしの検証";
        Width = 1200;
        Height = 420;
        Background = Brushes.Black;
        Loaded += async (_, _) => await VerifyAsync();
    }

    private async Task VerifyAsync()
    {
        try
        {
            const int width = 16;
            const int height = 12;
            var leftPixels = new byte[width * height * 4];
            var rightPixels = new byte[leftPixels.Length];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 4;
                var value = (byte)((x * 19 + y * 31) % 256);
                leftPixels[offset] = leftPixels[offset + 1] = leftPixels[offset + 2] = value;
                rightPixels[offset] = rightPixels[offset + 1] = rightPixels[offset + 2] =
                    (byte)Math.Clamp(value + (x % 3 == 0 ? 36 : -12), 0, 255);
                leftPixels[offset + 3] = rightPixels[offset + 3] = 255;
            }
            var left = new ComparisonFrame(width, height, 7, leftPixels);
            var right = new ComparisonFrame(width, height, 7, rightPixels);
            var panels = new UniformGrid { Rows = 1, Columns = 4 };
            var distinct = new HashSet<string>();
            foreach (var (label, radius) in new[] { ("ぼかしなし", 0), ("弱", 1), ("中", 2), ("強", 4) })
            {
                var result = FrameComparisonService.CreateHeatmap(left, right, 16, radius);
                var expected = ReferenceDifference(left, right, radius);
                if (Math.Abs(result.MeanAbsoluteRgbDifference - expected) > 0.000001)
                    throw new InvalidDataException($"{label}の平均差が一致しません。");
                if (FrameComparisonService.CreateHeatmap(left, left, 16, radius)
                    .MeanAbsoluteRgbDifference != 0)
                    throw new InvalidDataException($"{label}で同一画像に差が出ました。");
                distinct.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(result.Heatmap)));
                var panel = new DockPanel { Margin = new Thickness(8) };
                panel.Children.Add(new TextBlock
                {
                    Text = $"{label}（{radius * 2 + 1}×{radius * 2 + 1}）",
                    Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 8)
                });
                DockPanel.SetDock(panel.Children[0], Dock.Top);
                panel.Children.Add(new Image
                {
                    Source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32,
                        null, result.Heatmap, width * 4),
                    Stretch = Stretch.Fill
                });
                panels.Children.Add(panel);
            }
            if (distinct.Count != 4) throw new InvalidDataException("4段階の表示が区別できません。");
            Content = panels;
            SaveReport(new { Radii = new[] { 0, 1, 2, 4 },
                ReferenceMatched = true, IdenticalFramesZero = true, DistinctImages = true,
                Result = "合格" });
            await Task.Delay(500);
            Close();
        }
        catch (Exception exception)
        {
            SaveReport(new { ErrorCode = $"0x{exception.HResult:X8}", Result = "失敗" });
            Application.Current.Shutdown(1);
        }
    }

    private static double ReferenceDifference(ComparisonFrame left, ComparisonFrame right, int radius)
    {
        double total = 0;
        for (var y = 0; y < left.Height; y++)
        for (var x = 0; x < left.Width; x++)
        for (var channel = 0; channel < 3; channel++)
        {
            var sum = 0;
            var count = 0;
            for (var sourceY = Math.Max(0, y - radius); sourceY <= Math.Min(left.Height - 1, y + radius); sourceY++)
            for (var sourceX = Math.Max(0, x - radius); sourceX <= Math.Min(left.Width - 1, x + radius); sourceX++)
            {
                var offset = (sourceY * left.Width + sourceX) * 4 + channel;
                sum += left.Pixels[offset] - right.Pixels[offset];
                count++;
            }
            total += Math.Abs(sum) / (double)count;
        }
        return total / (left.Width * left.Height * 3);
    }

    private static void SaveReport(object report)
    {
        var workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var directory = Path.Combine(workspace, "measurement-results");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"heatmap-blur-{Guid.NewGuid():N}.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }
}
