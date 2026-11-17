using System.Windows;
using System.Windows.Controls;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public partial class MainWindow
{
    private Task VerifyGraphImageSaveAsync()
    {
        try
        {
            var reference = videos.Single(entry => entry.IsReference).Descriptor;
            var candidate = videos.Single(entry => !entry.IsReference).Descriptor;
            qualityResults[(candidate.Id, QualityMetric.Ssim)] = new QualityResult(QualityMetric.Ssim, 0.85,
                [new QualityFrame(0, 0.8), new QualityFrame(1, 0.9)]);
            RefreshQualityValues();
            qualityGraphPanel!.SelectMetric(QualityMetric.Ssim);
            Exception? windowError = null;
            var checkedWindow = false;
            _ = Dispatcher.BeginInvoke(async () =>
            {
                GraphImageSaveWindow? window = null;
                try
                {
                    await Task.Delay(400);
                    window = Application.Current.Windows.OfType<GraphImageSaveWindow>().Single();
                    window.UpdateLayout();
                    if (window.CurrentPreviewImage is not { PixelWidth: 1024, PixelHeight: 768 } ||
                        window.ReferenceFileName != reference.Name || window.CurrentMetric != QualityMetric.Ssim ||
                        window.FilePointCount != 1 || window.SelectedVideoCount != 1)
                        throw new InvalidOperationException("基準動画・評価結果・選択が保存画面に反映されません。");
                    checkedWindow = true;
                }
                catch (Exception error) { windowError = error; }
                finally { (window ?? Application.Current.Windows.OfType<GraphImageSaveWindow>().FirstOrDefault())?.Close(); }
            });
            SaveGraphImageMenuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            if (windowError is not null) throw new InvalidOperationException("保存画面を確認できません。", windowError);
            if (!checkedWindow || Application.Current.Windows.Count != 1)
                throw new InvalidOperationException("メニューから保存画面を開閉できません。");
            Console.WriteLine("主画面: グラフ画像保存メニュー・基準動画・評価結果・プレビューが合格");
            Application.Current.Shutdown(0);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Application.Current.Shutdown(1);
        }
        return Task.CompletedTask;
    }
}
