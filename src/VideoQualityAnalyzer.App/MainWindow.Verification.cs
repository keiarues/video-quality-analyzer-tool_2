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
            SortSelect.SelectedIndex = 1;
            if (!VideoList.SelectedItems.Contains(original[1]) || !VideoList.SelectedItems.Contains(original[2]))
                throw new InvalidOperationException("並べ替えで選択が失われました。");
            SortSelect.SelectedIndex = 0;
            if (!videos.SequenceEqual(original)) throw new InvalidOperationException("登録順に戻りません。");
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
            Console.WriteLine("主画面: 並べ替え・選択維持・元動画変更・削除時の評価結果破棄が合格");
            Application.Current.Shutdown(0);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            Application.Current.Shutdown(1);
        }
    }
}
