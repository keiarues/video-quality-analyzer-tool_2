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
                Play_Click(this, new RoutedEventArgs());
                var timeout = System.Diagnostics.Stopwatch.StartNew();
                while (comparisonPanel.CurrentFrameNumber < firstFrame + 5)
                {
                    if (timeout.Elapsed.TotalSeconds > 10) throw new InvalidOperationException("主画面で再生が進みません。");
                    await Task.Delay(20);
                }
                PausePlayback();
                var paused = comparisonPanel.CurrentFrameNumber;
                await Task.Delay(200);
                if (comparisonPanel.CurrentFrameNumber != paused) throw new InvalidOperationException("一時停止中にフレームが進みました。");
                FrameStep_Click(this, new RoutedEventArgs());
                await comparisonChangeTask;
                if (comparisonPanel.CurrentFrameNumber != paused + 1) throw new InvalidOperationException("1コマ送りが不正です。");
                await comparisonPanel.VerifyCurrentPixelsAsync();
                StopPlayback();
                await comparisonChangeTask;
                if (comparisonPanel.CurrentFrameNumber != 0 || comparisonPanel.IsPlaying)
                    throw new InvalidOperationException("停止時に先頭で静止しません。");
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

    private void VerifyMainFeatures()
    {
        try
        {
            if (videos.Count < 3) throw new InvalidOperationException("検証には3本以上の動画が必要です。");
            var original = videos.ToArray();
            VideoList.SelectedItems.Clear();
            VideoList.SelectedItems.Add(original[1]);
            VideoList.SelectedItems.Add(original[2]);
            VideoLibrary.SetGroup([original[1], original[2]], "検証グループ");
            VideoList_Sorting(this, new System.Windows.Controls.DataGridSortingEventArgs(
                VideoList.Columns.Single(column => column.SortMemberPath == "Name")));
            if (!VideoList.SelectedItems.Contains(original[1]) || !VideoList.SelectedItems.Contains(original[2]))
                throw new InvalidOperationException("並べ替えで選択が失われました。");
            activeSort = VideoSortOrder.Registration;
            sortDescending = false;
            RefreshVideoOrder();
            if (!videos.SequenceEqual(original)) throw new InvalidOperationException("登録順に戻りません。");
            if (VideoList.Columns.Count != 9 || original.Any(entry => entry.Descriptor.AverageBitrate is not > 0))
                throw new InvalidOperationException("動画情報の列または平均ビットレートが不足しています。");
            qualityResults[(original[1].Descriptor.Id, QualityMetric.Ssim)] = new QualityResult(QualityMetric.Ssim, 2, [new QualityFrame(0, 2)]);
            qualityResults[(original[2].Descriptor.Id, QualityMetric.Ssim)] = new QualityResult(QualityMetric.Ssim, 10, [new QualityFrame(0, 10)]);
            RefreshQualityValues();
            if (FindName("SortSelect") is not null ||
                !Equals(VideoList.Columns.Single(column => column.SortMemberPath == "Bitrate").Header, "kbps") ||
                VideoList.Columns.Where(column => column.SortMemberPath is "Resolution" or "FrameRate" or "Codec" or "Bitrate" or "Vmaf" or "Ssim" or "Psnr")
                    .Any(column => column.MaxWidth > 115))
                throw new InvalidOperationException("列の省略名・幅またはソート選択欄の削除が不正です。");
            UpdateLayout();
            if (VideoList.Columns.Sum(column => column.ActualWidth) > VideoList.ActualWidth - SystemParameters.VerticalScrollBarWidth)
                throw new InvalidOperationException("初期幅で全列が収まりません。");
            var bitrateColumn = (System.Windows.Controls.DataGridTextColumn)VideoList.Columns.Single(column => column.SortMemberPath == "Bitrate");
            if (bitrateColumn.Binding is not System.Windows.Data.Binding { StringFormat: "N0" })
                throw new InvalidOperationException("ビットレートの3桁区切りが設定されていません。");
            var scoreColumn = VideoList.Columns.Single(column => column.SortMemberPath == "Ssim");
            VideoList_Sorting(this, new System.Windows.Controls.DataGridSortingEventArgs(scoreColumn));
            if (videos[0] != original[1] || videos[1] != original[2])
                throw new InvalidOperationException("評価列の昇順が数値順になりません。");
            VideoList_Sorting(this, new System.Windows.Controls.DataGridSortingEventArgs(scoreColumn));
            if (videos[0] != original[2] || videos[1] != original[1] ||
                !VideoList.SelectedItems.Contains(original[1]) || !VideoList.SelectedItems.Contains(original[2]))
                throw new InvalidOperationException("評価列の降順または選択維持が不正です。");
            activeSort = VideoSortOrder.Registration;
            sortDescending = false;
            RefreshVideoOrder();
            var result = new QualityResult(QualityMetric.Ssim, 1, [new QualityFrame(0, 1)]);
            qualityResults[(original[2].Descriptor.Id, QualityMetric.Ssim)] = result;
            VideoList.SelectedItems.Clear();
            VideoList.SelectedItem = original[1];
            SetReference_Click(this, new RoutedEventArgs());
            if (qualityResults.Count != 0 || referenceId != original[1].Descriptor.Id ||
                videos.Count(entry => entry.IsReference) != 1)
                throw new InvalidOperationException("元動画変更時の結果破棄が不正です。");
            qualityResults[(original[0].Descriptor.Id, QualityMetric.Ssim)] = result;
            qualityResults[(original[2].Descriptor.Id, QualityMetric.Ssim)] = result;
            VideoList.SelectedItem = original[2];
            Remove_Click(this, new RoutedEventArgs());
            if (qualityResults.Count != 1 || !qualityResults.ContainsKey((original[0].Descriptor.Id, QualityMetric.Ssim)))
                throw new InvalidOperationException("比較動画削除時の結果破棄が不正です。");
            VideoList.SelectedItem = original[1];
            Remove_Click(this, new RoutedEventArgs());
            if (qualityResults.Count != 0 || videos.Count(entry => entry.IsReference) != 1)
                throw new InvalidOperationException("元動画削除時の結果破棄が不正です。");
            Console.WriteLine("主画面: 動画情報列・評価列の昇降順・選択維持・元動画変更・削除時の結果破棄が合格");
            Application.Current.Shutdown(0);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            Application.Current.Shutdown(1);
        }
    }
}
