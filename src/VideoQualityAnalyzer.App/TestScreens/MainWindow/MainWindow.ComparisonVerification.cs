using System.Windows;
using VideoQualityAnalyzer.Media;
using VideoQualityAnalyzer.Core;

namespace VideoQualityAnalyzer.App;

public partial class MainWindow
{
    private async Task VerifyEmbeddedComparisonAsync()
    {
        try
        {
            var entries = videos.ToArray();
            var reference = entries.Single(entry => entry.Descriptor.Codec == "ffv1");
            var hevc = entries.Where(entry => entry.Descriptor.Codec == "hevc").ToArray();
            var av1 = entries.Where(entry => entry.Descriptor.Codec == "av1").ToArray();
            var pairs = new[] { (reference, hevc[0]), (reference, hevc[1]), (reference, av1[0]),
                (reference, av1[1]), (hevc[0], hevc[1]), (av1[0], av1[1]) };
            foreach (var (first, second) in pairs)
            {
                referenceId = first.Descriptor.Id;
                VideoList.SelectedItems.Clear();
                VideoList.SelectedItem = second;
                UpdateSelection();
                await comparisonChangeTask;
                if (!comparisonActive || comparisonPanel is null || !comparisonPanel.IsReady ||
                    ComparisonHost.Content != comparisonPanel || Application.Current.Windows.Count != 1)
                    throw new InvalidOperationException("比較部品が主画面内に表示されていません。");
                ApplySeek(TimeSpan.FromSeconds(5));
                await comparisonChangeTask;
                var firstFrame = comparisonPanel.CurrentFrameNumber;
                foreach (var mode in new[] { 2, 1, 0 }) { comparisonPanel.SelectMode(mode); UpdateLayout(); }
                PlayPauseButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if (!Equals(PlayPauseButton.Content, "⏸")) throw new InvalidOperationException("再生中のボタン表示が不正です。");
                var timeout = System.Diagnostics.Stopwatch.StartNew();
                while (comparisonPanel.CurrentFrameNumber < firstFrame + 5)
                {
                    if (timeout.Elapsed.TotalSeconds > 10) throw new InvalidOperationException("主画面で再生が進みません。");
                    await Task.Delay(20);
                }
                PlayPauseButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if (!Equals(PlayPauseButton.Content, "▶")) throw new InvalidOperationException("一時停止中のボタン表示が不正です。");
                var paused = comparisonPanel.CurrentFrameNumber;
                await Task.Delay(200);
                if (comparisonPanel.CurrentFrameNumber != paused) throw new InvalidOperationException("一時停止中にフレームが進みました。");
                FrameStep_Click(this, new RoutedEventArgs());
                await comparisonChangeTask;
                if (comparisonPanel.CurrentFrameNumber != paused + 1) throw new InvalidOperationException("1コマ送りが不正です。");
                FrameBackButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                await comparisonChangeTask;
                if (comparisonPanel.CurrentFrameNumber != paused) throw new InvalidOperationException("1コマ戻しが不正です。");
                await comparisonPanel.VerifyCurrentPixelsAsync();
                StopPlayback();
                await comparisonChangeTask;
                if (comparisonPanel.CurrentFrameNumber != 0 || comparisonPanel.IsPlaying)
                    throw new InvalidOperationException("停止時に先頭で静止しません。");
                FrameBackButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                await comparisonChangeTask;
                if (comparisonPanel.CurrentFrameNumber != 0) throw new InvalidOperationException("先頭からのコマ戻しが不正です。");
                Console.WriteLine("主画面比較: 再生・タブ・一時停止・コマ送り・静止画素・停止が合格");
            }
            ApplySeek(TimeSpan.FromSeconds(5));
            VideoList.SelectedItem = hevc[0];
            VideoList.SelectedItem = av1[0];
            VideoList.SelectedItem = hevc[1];
            await comparisonChangeTask;
            if (comparisonRightId != hevc[1].Descriptor.Id || comparisonPanel is null || !comparisonPanel.IsReady)
                throw new InvalidOperationException("連続選択が最後の動画に反映されません。");
            await comparisonPanel.VerifyCurrentPixelsAsync();
            Console.WriteLine("主画面比較: 連続選択の最終結果と静止画素が合格");
            UpdateLayout();
            if (PlaybackControls.Parent != ComparisonHost.Parent ||
                PlaybackControls.TranslatePoint(new Point(0, 0), this).Y <
                ComparisonHost.TranslatePoint(new Point(0, ComparisonHost.ActualHeight), this).Y)
                throw new InvalidOperationException("再生制御がプレビュー直下にありません。");
            var buttonLabels = PlaybackButtons.Children.OfType<System.Windows.Controls.StackPanel>()
                .SelectMany(group => group.Children.OfType<System.Windows.Controls.Button>())
                .Select(button => (string)button.Content).ToArray();
            if (!buttonLabels.SequenceEqual(new[] { "■", "▶", "−5秒", "−1秒", "|◀", "▶|", "+1秒", "+5秒" }))
                throw new InvalidOperationException("再生ボタンの順番が不正です。");
            var snapshot = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight,
                96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            snapshot.Render((System.Windows.Media.Visual)Content);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(snapshot));
            var layoutPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,
                "../../../../../measurement-results/main-comparison-layout.png"));
            using (var stream = System.IO.File.Create(layoutPath)) encoder.Save(stream);
            var remaining = videos.First(entry => entry.Descriptor.Id == referenceId);
            VideoList.SelectedItems.Clear();
            foreach (var entry in videos.Where(entry => entry != remaining).ToArray()) VideoList.SelectedItems.Add(entry);
            Remove_Click(this, new RoutedEventArgs());
            if (comparisonActive || ComparisonHost.Visibility != Visibility.Collapsed ||
                comparisonPanel.IsReady || !left.Av1.IsOpen)
                throw new InvalidOperationException("1本表示への切り替えと比較処理の解放が不正です。");
            Console.WriteLine("主画面比較: 1本表示への切り替えと比較処理の解放が合格");
            Application.Current.Shutdown(0);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            Application.Current.Shutdown(1);
        }
    }

}
