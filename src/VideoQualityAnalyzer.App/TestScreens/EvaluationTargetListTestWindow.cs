using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public sealed class EvaluationTargetListTestWindow : Window
{
    public EvaluationTargetListTestWindow(bool automatic)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "評価対象動画一覧の検証"; Width = 1060; Height = 280;
        var root = new StackPanel { Margin = new Thickness(16) };
        Content = root;
        root.Children.Add(new TextBlock { Text = "元動画：検証用", Margin = new Thickness(4) });
        var targets = new EvaluationTargetListControl();
        root.Children.Add(targets);
        var entries = Enumerable.Range(0, 4).Select(index => new VideoEntry(new VideoDescriptor(
            $"test-{index}", $"検証動画{index}.mkv", 1920, 1080, "hevc", 30, 30,
            TimeSpan.FromSeconds(10), false, 300, 1))).ToArray();
        entries[0].IsReference = true;
        targets.SetEntries(entries, [entries[2].Descriptor.Id]);
        if (automatic) Loaded += (_, _) =>
        {
            try
            {
                if (targets.List.Items.Count != 3 || targets.List.Columns.Count != 9 ||
                    !targets.SelectedVideos.Select(video => video.Id).SequenceEqual([entries[2].Descriptor.Id]))
                    throw new InvalidOperationException("評価対象一覧の初期表示または選択が不正です。");
                targets.List.SelectedItems.Add(entries[1]);
                if (targets.SelectedVideos.Length != 2) throw new InvalidOperationException("複数動画を選択できません。");
                targets.List.UnselectAll();
                if (targets.SelectedVideos.Length != 0) throw new InvalidOperationException("全動画の選択解除ができません。");
                targets.SetEntries(entries);
                if (targets.SelectedVideos.Length != 3) throw new InvalidOperationException("初期状態で全動画を選択できません。");
                var settings = new EvaluationSettingsWindow(entries[0].Descriptor, EvaluationSelection.Full,
                    comparisonVideos: entries, selectedVideoIds: [entries[2].Descriptor.Id]) { Owner = this };
                Exception? verificationError = null;
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (!settings.SelectedVideos.Select(video => video.Id).SequenceEqual([entries[2].Descriptor.Id]))
                            throw new InvalidOperationException("主画面の選択を設定画面へ引き継げません。");
                        settings.SelectVideosForVerification([]);
                        settings.ApplyForVerification(EvaluationSelection.Full);
                        if (settings.DialogResult is not null)
                            throw new InvalidOperationException("評価対象を選ばずに実行できました。");
                        settings.SelectVideosForVerification([entries[1].Descriptor.Id, entries[3].Descriptor.Id]);
                        settings.VerifyStableRangeLayout();
                        var image = new RenderTargetBitmap((int)settings.ActualWidth, (int)settings.ActualHeight,
                            96, 96, PixelFormats.Pbgra32);
                        image.Render((Visual)settings.Content);
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(image));
                        var directory = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,
                            "../../../../../measurement-results/evaluation-target-list"));
                        System.IO.Directory.CreateDirectory(directory);
                        using (var stream = System.IO.File.Create(System.IO.Path.Combine(directory, "settings.png"))) encoder.Save(stream);
                        settings.ApplyForVerification(EvaluationSelection.Full);
                    }
                    catch (Exception error) { verificationError = error; settings.Close(); }
                }));
                var accepted = settings.ShowDialog();
                if (verificationError is not null) throw verificationError;
                if (accepted != true ||
                    !settings.SelectedVideos.Select(video => video.Id).SequenceEqual([entries[1].Descriptor.Id, entries[3].Descriptor.Id]))
                    throw new InvalidOperationException("設定画面で選択した評価対象を返せません。");
                Console.WriteLine("評価対象動画一覧: 9列・初期選択・複数選択・全解除・未選択時の実行禁止・選択結果が合格");
                Application.Current.Shutdown(0);
            }
            catch (Exception error) { Console.Error.WriteLine(error); Application.Current.Shutdown(1); }
        };
    }
}
