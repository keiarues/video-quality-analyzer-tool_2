using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VideoQualityAnalyzer.GpuPreview;

// 本番の比較画面へ入れる前に、映像範囲とドラッグ位置の対応を確認する専用画面。
public sealed class WipeDragTestWindow : Window
{
    private readonly Grid viewport = new() { Background = Brushes.Black, ClipToBounds = true };
    private readonly Grid rightLayer = new() { Background = Brushes.SteelBlue };
    private readonly Grid handle = new()
    {
        Width = 24, Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Left,
        Cursor = Cursors.SizeWE
    };
    private readonly TextBlock positionText = new()
    {
        Margin = new Thickness(8), Foreground = Brushes.White,
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top
    };
    private WipeBoundaryController? controller;

    public WipeDragTestWindow()
    {
        Title = "ワイプ境界ドラッグの検証";
        Width = 1100;
        Height = 700;
        var line = new Border
        {
            Width = 3, Background = Brushes.Gold, HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false
        };
        handle.Children.Add(line);
        viewport.Children.Add(new Grid { Background = Brushes.IndianRed });
        viewport.Children.Add(rightLayer);
        viewport.Children.Add(positionText);
        viewport.Children.Add(handle);
        Content = viewport;
        Loaded += async (_, _) => await VerifyAsync();
    }

    private async Task VerifyAsync()
    {
        try
        {
            controller = new WipeBoundaryController(viewport, rightLayer, handle,
                positionText, () => 4d / 3);
            foreach (var percent in new[] { 0d, 25d, 50d, 75d, 100d })
            {
                var x = controller.ImageLeft + controller.ImageWidth * percent / 100;
                controller.SetFromViewportX(x);
                if (Math.Abs(controller.Fraction * 100 - percent) > 0.001 ||
                    rightLayer.Clip is not RectangleGeometry clip ||
                    Math.Abs(clip.Rect.Left - handle.Margin.Left - handle.Width / 2) > 0.5 ||
                    handle.Visibility != Visibility.Visible)
                    throw new InvalidOperationException($"境界{percent}%の位置が一致しません。");
            }
            controller.SetFromViewportX(controller.ImageLeft - 100);
            if (controller.Fraction != 0) throw new InvalidOperationException("左端で止まりません。");
            controller.SetFromViewportX(controller.ImageLeft + controller.ImageWidth + 100);
            if (controller.Fraction != 1) throw new InvalidOperationException("右端で止まりません。");
            controller.SetFraction(0.5);
            SaveReport(new { Positions = new[] { 0, 25, 50, 75, 100 },
                EndpointsClamped = true, BoundaryAligned = true, Result = "合格" });
            await Task.Delay(350);
            Close();
        }
        catch (Exception exception)
        {
            SaveReport(new { ErrorCode = $"0x{exception.HResult:X8}", Result = "失敗" });
            Application.Current.Shutdown(1);
        }
    }

    private static void SaveReport(object report)
    {
        var workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var directory = Path.Combine(workspace, "measurement-results");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"wipe-drag-{Guid.NewGuid():N}.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }
}
