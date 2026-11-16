using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public sealed class EvaluationTargetSummaryTestWindow : Window
{
    public EvaluationTargetSummaryTestWindow(bool automatic)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "評価対象動画の表示検証";
        Width = 500; Height = 200;
        if (automatic) Loaded += (_, _) =>
        {
            try
            {
                var reference = Video("reference", "基準動画.mkv");
                var videos = Enumerable.Range(1, 6).Select(index => Video($"video-{index}", $"比較動画{index}.mkv")).ToArray();
                var shortList = new EvaluationSettingsWindow(reference, EvaluationSelection.Full,
                    comparisonVideos: videos.Take(3));
                if (!shortList.TargetSummaryText.Split('\n').SequenceEqual(
                    new[] { "評価対象動画" }.Concat(videos.Take(3).Select(video => video.Name))))
                    throw new InvalidOperationException("5本以下の表示が不正です。");
                shortList.Close();
                var settings = new EvaluationSettingsWindow(reference, EvaluationSelection.Full,
                    comparisonVideos: videos) { Owner = this };
                Exception? verificationError = null;
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var summary = settings.TargetSummaryText;
                        if (!summary.Split('\n').SequenceEqual(new[] { "評価対象動画" }
                            .Concat(videos.Take(5).Select(video => video.Name))
                            .Append("全6ファイルを評価計算します")))
                            throw new InvalidOperationException("先頭5本と総数の表示が不正です。");
                        settings.VerifyStableRangeLayout();
                        var directory = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,
                            "../../../../../measurement-results/evaluation-target-summary"));
                        System.IO.Directory.CreateDirectory(directory);
                        foreach (var (name, selection) in new (string Name, EvaluationSelection Selection)[]
                        {
                            ("full", EvaluationSelection.Full),
                            ("time", new(PartialEvaluationMode.TimeRange, 0.2, 0.7)),
                            ("periodic", new(PartialEvaluationMode.Periodic, PeriodSeconds: 1, LengthSeconds: 0.25)),
                            ("uniform", new(PartialEvaluationMode.Uniform, LengthSeconds: 0.25, Count: 2))
                        })
                        {
                            settings.PreviewSelectionForVerification(selection);
                            settings.UpdateLayout();
                            var image = new RenderTargetBitmap((int)settings.ActualWidth, (int)settings.ActualHeight,
                                96, 96, PixelFormats.Pbgra32);
                            image.Render((Visual)settings.Content);
                            var encoder = new PngBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(image));
                            using var stream = System.IO.File.Create(System.IO.Path.Combine(directory, $"settings-{name}.png"));
                            encoder.Save(stream);
                        }
                        settings.ApplyForVerification(EvaluationSelection.Full);
                    }
                    catch (Exception error) { verificationError = error; settings.Close(); }
                }));
                var accepted = settings.ShowDialog();
                if (verificationError is not null) throw verificationError;
                if (accepted != true || settings.SelectedVideos.Length != 6)
                    throw new InvalidOperationException("表示した評価対象と実行対象が一致しません。");
                Console.WriteLine("評価対象動画表示: 先頭5本・6本目の省略・総数・実行対象が合格");
                Application.Current.Shutdown(0);
            }
            catch (Exception error) { Console.Error.WriteLine(error); Application.Current.Shutdown(1); }
        };
    }

    private static VideoDescriptor Video(string id, string name) => new(id, name, 64, 64, "ffv1", 30, 30,
        TimeSpan.FromSeconds(1), false, 30, 1);
}
