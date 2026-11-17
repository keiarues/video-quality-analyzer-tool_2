using System.Windows;
using VideoQualityAnalyzer.Media;
using VideoQualityAnalyzer.Core;

namespace VideoQualityAnalyzer.App;

public partial class MainWindow
{
    private async Task VerifyPreviewControlsAsync()
    {
        try
        {
            var reference = videos.First(entry => entry.Descriptor.Id == referenceId);
            var candidate = videos.First(entry => entry != reference && CanCompare(reference.Descriptor, entry.Descriptor));
            VideoList.SelectedItem = candidate;
            await comparisonChangeTask;
            PausePlayback();
            VolumeSlider.Value = 0.25;
            MuteCheck.IsChecked = true;
            if (Math.Abs(LeftMedia.Volume - 0.25) > 0.001 || !LeftMedia.IsMuted || !RightMedia.IsMuted)
                throw new InvalidOperationException("左右の音量・消音設定が不正です。");
            MuteCheck.IsChecked = false;
            if (LeftMedia.IsMuted || !RightMedia.IsMuted)
                throw new InvalidOperationException("左の消音解除時に右音声も解除されました。");
            var graphHeight = QualityGraphRow.Height;
            GraphToggle_Click(this, new RoutedEventArgs());
            UpdateLayout();
            if (QualityGraphHost.IsVisible || QualityGraphRow.ActualHeight != 0) throw new InvalidOperationException("グラフを最小化できません。");
            GraphToggle_Click(this, new RoutedEventArgs());
            UpdateLayout();
            if (!QualityGraphHost.IsVisible || QualityGraphRow.Height != graphHeight) throw new InvalidOperationException("グラフを復元できません。");
            foreach (var mode in new[] { 0, 1, 2 })
            foreach (var playing in new[] { false, true })
            {
                comparisonPanel!.SelectMode(mode);
                ApplySeek(TimeSpan.FromSeconds(0.2));
                await comparisonChangeTask;
                if (playing) Play_Click(this, new RoutedEventArgs()); else PausePlayback();
                var positionBeforeChange = CurrentPosition();
                comparisonPanel.SelectPreviewSource(true, candidate.Descriptor.Path);
                await comparisonChangeTask;
                comparisonPanel.SelectPreviewSource(false, reference.Descriptor.Path);
                await comparisonChangeTask;
                if (left.Video != candidate.Descriptor || right.Video != reference.Descriptor || referenceId != reference.Descriptor.Id || isPlaying != playing)
                    throw new InvalidOperationException("プレビューの動画変更、元動画維持または再生状態が不正です。");
                if (!RightMedia.IsMuted)
                    throw new InvalidOperationException("動画切り替え後に右音声の消音が解除されました。");
                if (!playing && Math.Abs((CurrentPosition() - positionBeforeChange).TotalSeconds) > 0.05)
                    throw new InvalidOperationException("動画切り替えで共通再生時刻が変わりました。");
                comparisonPanel.SelectPreviewSource(true, reference.Descriptor.Path);
                await comparisonChangeTask;
                comparisonPanel.SelectPreviewSource(false, candidate.Descriptor.Path);
                await comparisonChangeTask;
            }
            PausePlayback();
            PreviewLeftSelect.SelectedItem = candidate;
            await comparisonChangeTask;
            if (left.Video != candidate.Descriptor) throw new InvalidOperationException("通常画面の選択連動が不正です。");
            var status = WindowStatusText.Text;
            ReportComparisonFailure(comparisonGeneration - 1, new InvalidOperationException());
            if (WindowStatusText.Text != status)
                throw new InvalidOperationException("古い比較読み込みの失敗が現在の状態表示を上書きしました。");
            ReportComparisonFailure(comparisonGeneration, new InvalidOperationException());
            if (!WindowStatusText.Text.Contains("比較映像を準備できません"))
                throw new InvalidOperationException("現在の比較読み込み失敗を表示できません。");
            SetWindowStatus(status);
            var outputDirectory = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../../measurement-results"));
            foreach (var mode in new[] { 0, 1, 2 })
            {
                comparisonPanel!.SelectMode(mode);
                UpdateLayout();
                var image = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                image.Render((System.Windows.Media.Visual)Content);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                using var stream = System.IO.File.Create(System.IO.Path.Combine(outputDirectory, $"preview-controls-{mode}.png"));
                encoder.Save(stream);
            }
            Console.WriteLine("プレビュー操作: 左右選択、共通時刻、右音声消音、再生状態、グラフ最小化復元が合格");
            Application.Current.Shutdown(0);
        }
        catch (Exception exception) { Console.Error.WriteLine(exception.Message); Application.Current.Shutdown(1); }
    }

    private async Task VerifyPlaybackRefreshAsync()
    {
        try
        {
            if (comparisonActive || left.Video is null) throw new InvalidOperationException("通常プレビューの動画1本で起動してください。");
            displayTimer.Stop();
            Play_Click(this, new RoutedEventArgs());
            await Task.Delay(600);
            if (left.LastShownPosition is not { } shown || shown <= TimeSpan.Zero ||
                Math.Abs(SeekSlider.Value - shown.TotalSeconds) > 0.001 ||
                qualityGraphPanel is null || Math.Abs(qualityGraphPanel.PlayheadPosition.TotalSeconds - shown.TotalSeconds) > 0.001)
                throw new InvalidOperationException("通常プレビューの反映フレーム時刻と再生表示が一致しません。");
            PausePlayback();
            // 一時停止要求時に反映待ちだった画像通知が完了してから停止位置を比較する。
            await Task.Delay(150);
            var frozen = SeekSlider.Value;
            await Task.Delay(150);
            if (Math.Abs(SeekSlider.Value - frozen) > 0.001) throw new InvalidOperationException("一時停止中に表示位置が進んでいます。");
            Console.WriteLine("通常プレビュー: 定期タイマー停止中の反映フレーム時刻・グラフ・再生制御の一致と一時停止が合格");
            Application.Current.Shutdown(0);
        }
        catch (Exception exception) { Console.Error.WriteLine(exception.Message); Application.Current.Shutdown(1); }
    }

    private async Task VerifySeekSliderAsync()
    {
        try
        {
            var candidate = videos.First(entry => !entry.IsReference && entry.Descriptor.Codec == "hevc");
            VideoList.SelectedItem = candidate;
            await comparisonChangeTask;
            if (!comparisonActive || comparisonPanel is null) throw new InvalidOperationException("シーク検証用の比較映像がありません。");
            foreach (var playing in new[] { false, true })
            {
                PausePlayback();
                ApplySeek(TimeSpan.FromSeconds(1));
                await comparisonChangeTask;
                if (playing) Play_Click(this, new RoutedEventArgs());
                SeekSlider.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0)
                    { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragStartedEvent });
                SeekSlider.Value = 5;
                await Task.Delay(250);
                if (Math.Abs(SeekSlider.Value - 5) > 0.001)
                    throw new InvalidOperationException("表示更新でドラッグ位置が戻りました。");
                SeekSlider.Value = 12;
                await Task.Delay(250);
                if (Math.Abs(SeekSlider.Value - 12) > 0.001)
                    throw new InvalidOperationException("ドラッグ中の最新位置が失われました。");
                SeekSlider.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(0, 0, false)
                    { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragCompletedEvent });
                await comparisonChangeTask;
                if (Math.Abs(comparisonPanel.Position.TotalSeconds - 12) > 0.2 || comparisonPanel.IsPlaying != playing)
                    throw new InvalidOperationException("ドラッグ終了位置または再生状態が反映されません。");
                PausePlayback();
            }
            // 確定処理の途中で次の位置を要求しても、最後の指定だけを反映する。
            SeekSlider.Value = 6;
            CommitSeekSlider();
            SeekSlider.Value = 18;
            CommitSeekSlider();
            await comparisonChangeTask;
            if (Math.Abs(comparisonPanel.Position.TotalSeconds - 18) > 0.03 || Math.Abs(SeekSlider.Value - 18) > 0.03)
                throw new InvalidOperationException("連続したシークが最後の位置に到達しません。");
            SeekSlider.Value = 9;
            await Task.Delay(500);
            await comparisonChangeTask;
            if (Math.Abs(comparisonPanel.Position.TotalSeconds - 9) > 0.03)
                throw new InvalidOperationException("キー操作の位置が確定されません。");
            Console.WriteLine("シークバー: 再生中・一時停止中のドラッグ位置保持、終了位置への移動、再生状態維持、連続要求、キー操作が合格");
            Application.Current.Shutdown(0);
        }
        catch (Exception exception) { Console.Error.WriteLine(exception.Message); Application.Current.Shutdown(1); }
    }

}
