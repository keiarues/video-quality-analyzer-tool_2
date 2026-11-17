using System.Windows;
using VideoQualityAnalyzer.Media;
using VideoQualityAnalyzer.Core;

namespace VideoQualityAnalyzer.App;

public partial class MainWindow
{
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
            if (ReferenceList.Items.Count != 1 || ReferenceList.Items[0] != original[0] ||
                VideoList.Items.Count != videos.Count - 1 || VideoList.Items.Contains(original[0]) ||
                !ReferenceList.Columns.Select(column => column.SortMemberPath).SequenceEqual(VideoList.Columns.Select(column => column.SortMemberPath)))
                throw new InvalidOperationException("元動画の専用一覧または列構成が不正です。");
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
            var confirmationCount = 0;
            confirmEvaluationResetForVerification = message =>
            {
                if (!message.Contains("基準動画を変更すると")) throw new InvalidOperationException("基準動画変更の警告文が不正です。");
                confirmationCount++;
                return false;
            };
            SetReference_Click(this, new RoutedEventArgs());
            if (referenceId != original[0].Descriptor.Id || !ReferenceEquals(result, qualityResults[(original[2].Descriptor.Id, QualityMetric.Ssim)]) ||
                confirmationCount != 1) throw new InvalidOperationException("基準動画変更のキャンセルで結果または選択が変わりました。");
            confirmEvaluationResetForVerification = _ => true;
            SetReference_Click(this, new RoutedEventArgs());
            if (qualityResults.Count != 0 || referenceId != original[1].Descriptor.Id ||
                videos.Count(entry => entry.IsReference) != 1)
                throw new InvalidOperationException("元動画変更時の結果破棄が不正です。");
            if (ReferenceList.Items.Count != 1 || ReferenceList.Items[0] != original[1] ||
                VideoList.Items.Contains(original[1]) || !VideoList.Items.Contains(original[0]))
                throw new InvalidOperationException($"元動画変更時に上下の一覧が更新されません。上={ReferenceList.Items.Count} / {ReferenceList.Items.Cast<VideoEntry>().FirstOrDefault()?.Descriptor.Name} 下={string.Join(",", VideoList.Items.Cast<VideoEntry>().Select(item => item.Descriptor.Name))}");
            if (VideoList.SelectedItems.Count != 0 || ReferenceList.SelectedItems.Count != 0)
                throw new InvalidOperationException($"元動画変更後に一覧の選択が残っています。対象={VideoList.SelectedItems.Count}、基準={ReferenceList.SelectedItems.Count}");
            qualityResults[(original[0].Descriptor.Id, QualityMetric.Ssim)] = result;
            qualityResults[(original[2].Descriptor.Id, QualityMetric.Ssim)] = result;
            VideoList.SelectedItem = original[2];
            confirmEvaluationResetForVerification = _ => throw new InvalidOperationException("比較動画の削除で警告が表示されました。");
            Remove_Click(this, new RoutedEventArgs());
            if (qualityResults.Count != 1 || !qualityResults.ContainsKey((original[0].Descriptor.Id, QualityMetric.Ssim)))
                throw new InvalidOperationException("比較動画削除時の結果破棄が不正です。");
            ReferenceList.SelectedItem = original[1];
            confirmEvaluationResetForVerification = message =>
            {
                if (!message.Contains("基準動画を削除すると")) throw new InvalidOperationException("基準動画削除の警告文が不正です。");
                confirmationCount++;
                return false;
            };
            Remove_Click(this, new RoutedEventArgs());
            if (!videos.Contains(original[1]) || !ReferenceEquals(result, qualityResults[(original[0].Descriptor.Id, QualityMetric.Ssim)]) ||
                confirmationCount != 2) throw new InvalidOperationException("基準動画削除のキャンセルで動画または結果が変わりました。");
            confirmEvaluationResetForVerification = _ => true;
            Remove_Click(this, new RoutedEventArgs());
            confirmEvaluationResetForVerification = null;
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
