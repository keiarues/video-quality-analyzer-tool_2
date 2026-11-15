using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VideoQualityAnalyzer.App;

public sealed class QualityMetricCardsTestWindow : Window
{
    public QualityMetricCardsTestWindow(bool automatic)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "評価指標カードの検証"; Width = 1060; Height = 370; MinWidth = 700; MinHeight = 320;
        var root = new StackPanel { Margin = new Thickness(16), Background = (Brush)FindResource("BackgroundBrush") };
        Content = root;
        root.Children.Add(new TextBlock { Text = "評価指標", Style = (Style)FindResource("UiTitleStyle"), Margin = new Thickness(4, 0, 4, 8) });
        var cards = new QualityMetricSelectionControl(); root.Children.Add(cards);
        if (automatic) Loaded += (_, _) =>
        {
            try
            {
                cards.VerifyMultipleSelection(); UpdateLayout();
                var image = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
                image.Render((Visual)Content);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../measurement-results/metric-cards"));
                Directory.CreateDirectory(directory);
                using (var stream = File.Create(Path.Combine(directory, "screen.png"))) encoder.Save(stream);
                Console.WriteLine("評価指標カード：複数選択・独立した解除・全解除・選択復元が合格");
                Application.Current.Shutdown(0);
            }
            catch (Exception error) { Console.Error.WriteLine(error); Application.Current.Shutdown(1); }
        };
    }
}
