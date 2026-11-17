using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

internal sealed partial class GraphImageSaveWindow
{
    private static string PngFileName(DateTime savedAt, string referenceName, QualityMetric metric, GraphImageTarget target)
    {
        var graphType = target == GraphImageTarget.Frames ? "フレームグラフ" : "ファイルグラフ";
        return $"{savedAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}_{referenceName}_" +
            $"{graphType}_{metric.ToString().ToUpperInvariant()}.png";
    }
    private SaveFileDialog CreateSaveDialog(DateTime savedAt) => new()
    {
        Filter = "PNG画像 (*.png)|*.png", DefaultExt = ".png", AddExtension = true,
        InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        FileName = PngFileName(savedAt, referenceFileName, graph.SelectedMetric,
            (GraphImageTarget)target.SelectedIndex)
    };
    private double FontScale() => new[] { 0.5, 0.8, 1, 1.2, 1.5, 2 }[font.SelectedIndex];
    private void UpdateSizeInputs()
    {
        dimension.IsEnabled = size.SelectedIndex == 1;
        heightDimension.IsEnabled = size.SelectedIndex == 1;
    }
    private (int Width, int Height) ImageSize()
    {
        var available = AvailableSize();
        var dpi = VisualTreeHelper.GetDpi(this);
        double width, height;
        if (size.SelectedIndex >= 2) return presets[size.SelectedIndex - 2];
        if (size.SelectedIndex == 0)
        {
            width = available.Width * dpi.DpiScaleX;
            height = available.Height * dpi.DpiScaleY;
        }
        else
        {
            static int Parse(string text)
            {
                if (!int.TryParse(text, out var value) || value < 1 || value > 16384)
                    throw new InvalidOperationException("幅と高さは1～16384ピクセルの整数で入力してください。");
                return value;
            }
            width = Parse(dimension.Text);
            height = Parse(heightDimension.Text);
        }
        if (width > 16384 || height > 16384 || width * height > 40_000_000)
            throw new InvalidOperationException("画像は各辺16384ピクセル、総画素数4000万以下にしてください。");
        return (Math.Max(1, (int)Math.Round(width)), Math.Max(1, (int)Math.Round(height)));
    }
    private BitmapSource CreateImage()
    {
        var (width, height) = ImageSize();
        return graph.CreateExportImage((GraphImageTarget)target.SelectedIndex, width, height, FontScale());
    }
    private BitmapSource SavePng(string path)
    {
        // ズームは表示だけに使用し、保存には元の出力画像を使う。
        RefreshPreview();
        var bitmap = preview.Source as BitmapSource ?? CreateImage();
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
        return bitmap;
    }
}
