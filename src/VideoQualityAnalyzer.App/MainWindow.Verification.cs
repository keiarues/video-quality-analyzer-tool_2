using System.Windows;
using VideoQualityAnalyzer.Media;
using VideoQualityAnalyzer.Core;

namespace VideoQualityAnalyzer.App;

public partial class MainWindow
{
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

    private async Task VerifyMainEvaluationAsync()
    {
        try
        {
            var candidate = videos.First(entry => !entry.IsReference);
            VideoList.SelectedItem = candidate;
            await comparisonChangeTask;
            if (VmafCheck.IsChecked == true || SsimCheck.IsChecked != true || PsnrCheck.IsChecked == true ||
                new[] { VmafCheck, SsimCheck, PsnrCheck }.Any(check => check.ToolTip is not System.Windows.Controls.TextBlock))
                throw new InvalidOperationException("主画面の初期指標または説明が不正です。");
            EvaluationStartButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (evaluationCancellation is null || !Equals(EvaluationStartButton.Content, "評価を中止") ||
                AddVideoButton.IsEnabled || SetReferenceButton.IsEnabled || RemoveVideoButton.IsEnabled || Application.Current.Windows.Count != 1)
                throw new InvalidOperationException("主画面での評価開始状態が不正です。");
            await evaluationTask;
            var key = (candidate.Descriptor.Id, QualityMetric.Ssim);
            if (!qualityResults.TryGetValue(key, out var completed) || completed.Frames.Count != 30 ||
                !Equals(EvaluationStartButton.Content, "評価を開始") || !EvaluationStartButton.IsEnabled)
                throw new InvalidOperationException("主画面で評価結果が確定されません。");
            qualityGraphPanel!.SelectMetric(QualityMetric.Ssim);
            if (qualityGraphPanel.FilePointCount != 1) throw new InvalidOperationException("評価結果が主画面グラフに反映されません。");
            EvaluationStartButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            await evaluationTask;
            if (!ReferenceEquals(completed, qualityResults[key])) throw new InvalidOperationException("完了済み結果が再計算されました。");
            PsnrCheck.IsChecked = true;
            EvaluationStartButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (evaluationCancellation is null) throw new InvalidOperationException("中止試験の評価が始まりません。");
            EvaluationStartButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            await evaluationTask;
            if (qualityResults.ContainsKey((candidate.Descriptor.Id, QualityMetric.Psnr)) ||
                !ReferenceEquals(completed, qualityResults[key]) || !EvaluationStartButton.IsEnabled ||
                !Equals(EvaluationStartButton.Content, "評価を開始"))
                throw new InvalidOperationException("評価中止後の結果またはボタン状態が不正です。");
            UpdateLayout();
            var checks = new[] { VmafCheck, SsimCheck, PsnrCheck };
            var expectedText = ((System.Windows.Media.SolidColorBrush)FindResource("TextBrush")).Color;
            if (checks.Append(MuteCheck).Any(check => check.Foreground is not System.Windows.Media.SolidColorBrush brush || brush.Color != expectedText))
                throw new InvalidOperationException("評価チェックまたはミュートの文字色が共通の明るい文字色になっていません。");
            if (checks[0].TranslatePoint(new Point(), this).Y >= checks[1].TranslatePoint(new Point(), this).Y ||
                checks[1].TranslatePoint(new Point(), this).Y >= checks[2].TranslatePoint(new Point(), this).Y ||
                checks.Any(check => Math.Abs(check.TranslatePoint(new Point(), this).X - checks[0].TranslatePoint(new Point(), this).X) > 1))
                throw new InvalidOperationException("指標のチェックが縦に並んでいません。");
            if (FontFamily.Source != "Yu Gothic UI" || FontSize != (double)FindResource("UiFontMedium") ||
                checks.Any(check => check.FontFamily.Source != FontFamily.Source || check.FontSize != FontSize) ||
                (double)FindResource("UiFontSmall") >= FontSize || (double)FindResource("UiFontLarge") <= FontSize)
                throw new InvalidOperationException("共通フォントのプリセットが適用されていません。");
            if (EvaluationControls.TranslatePoint(new Point(0, 0), this).Y <
                VideoList.TranslatePoint(new Point(0, VideoList.ActualHeight), this).Y)
                throw new InvalidOperationException("評価操作がファイル一覧の下にありません。");
            var directory = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,
                "../../../../../measurement-results"));
            await ExportQualityCsvAsync(System.IO.Path.Combine(directory, "main-evaluation-summary.csv"), false);
            await ExportQualityCsvAsync(System.IO.Path.Combine(directory, "main-evaluation-frames.csv"), true);
            var bytes = await System.IO.File.ReadAllBytesAsync(System.IO.Path.Combine(directory, "main-evaluation-summary.csv"));
            if (!bytes.AsSpan(0, 3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF })) throw new InvalidOperationException("CSVのBOMがありません。");
            var snapshot = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight,
                96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            snapshot.Render((System.Windows.Media.Visual)Content);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(snapshot));
            using (var stream = System.IO.File.Create(System.IO.Path.Combine(directory, "main-evaluation-layout.png"))) encoder.Save(stream);
            Console.WriteLine("主画面評価: 一覧下の配置・開始／中止切り替え・完了結果・再利用・中止・グラフ連動・CSV機能保持が合格");
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

    private async Task VerifyEmbeddedGraphsAsync()
    {
        try
        {
            var candidates = videos.Where(entry => !entry.IsReference).Take(2).ToArray();
            if (candidates.Length != 2 || qualityGraphPanel is null) throw new InvalidOperationException("グラフ検証には比較動画2本が必要です。");
            if (qualityResults.Count != 0) throw new InvalidOperationException("未評価の検証は空の評価結果で開始してください。");
            RefreshEmbeddedGraphs();
            qualityGraphPanel.BeginPlayheadDrag(0.25);
            qualityGraphPanel.EndPlayheadDrag();
            await comparisonChangeTask;
            if (Math.Abs(CurrentPosition().TotalSeconds - CommonDuration().TotalSeconds * 0.25) > 0.05)
                throw new InvalidOperationException("未評価のフレームグラフからシークできません。");
            foreach (var entry in candidates)
                qualityResults[(entry.Descriptor.Id, QualityMetric.Ssim)] = new QualityResult(QualityMetric.Ssim, 0.85,
                    [new QualityFrame(0, 0.8), new QualityFrame(10, 0.9), new QualityFrame(20, 0.85)]);
            RefreshQualityValues();
            VideoList.SelectedItems.Clear();
            VideoList.SelectedItem = candidates[0];
            await comparisonChangeTask;
            qualityGraphPanel.SelectMetric(QualityMetric.Ssim);
            UpdateLayout();
            if (QualityGraphHost.Content != qualityGraphPanel || Application.Current.Windows.Count != 1 ||
                qualityGraphPanel.FilePointCount != 2 || !qualityGraphPanel.DisplayedVideoIds.SequenceEqual(new[] { candidates[0].Descriptor.Id }))
                throw new InvalidOperationException("主画面の評価結果または選択がグラフに反映されません。");
            if (QualityGraphHost.TranslatePoint(new Point(0, 0), this).Y <
                PlaybackControls.TranslatePoint(new Point(0, PlaybackControls.ActualHeight), this).Y)
                throw new InvalidOperationException("品質グラフが再生制御の下にありません。");
            var originalGraphHeight = QualityGraphHost.ActualHeight;
            var originalControlHeight = PlaybackControls.ActualHeight;
            var originalPreviewHeight = ComparisonHost.ActualHeight;
            QualityGraphResizeBar.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(0, -30)
                { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragDeltaEvent });
            UpdateLayout();
            if (Math.Abs(QualityGraphHost.ActualHeight - originalGraphHeight - 30) > 1 ||
                Math.Abs(PlaybackControls.ActualHeight - originalControlHeight) > 1 ||
                Math.Abs(ComparisonHost.ActualHeight - originalPreviewHeight + 30) > 1)
                throw new InvalidOperationException("高さ変更がグラフと映像に反映されず、再生制御の高さが変化しています。");
            QualityGraphResizeBar.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(0, 30)
                { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragDeltaEvent });
            UpdateLayout();
            if (Math.Abs(QualityGraphHost.ActualHeight - originalGraphHeight) > 1 ||
                Math.Abs(PlaybackControls.ActualHeight - originalControlHeight) > 1)
                throw new InvalidOperationException("グラフ縮小時に再生制御の高さが変化しています。");
            qualityGraphPanel.SelectFile(candidates[1].Descriptor.Id);
            await comparisonChangeTask;
            if (VideoList.SelectedItem != candidates[1] ||
                !qualityGraphPanel.DisplayedVideoIds.SequenceEqual(new[] { candidates[1].Descriptor.Id }))
                throw new InvalidOperationException("グラフのファイル選択が主画面に反映されません。");
            qualityGraphPanel.SeekToFraction(0.5);
            await comparisonChangeTask;
            var expected = 10 / candidates[1].Descriptor.AverageFrameRate;
            if (Math.Abs(CurrentPosition().TotalSeconds - expected) > 0.001)
                throw new InvalidOperationException("グラフから主画面へシークできません。");
            UpdateTimeDisplay();
            if (Math.Abs(qualityGraphPanel.PlayheadPosition.TotalSeconds - CurrentPosition().TotalSeconds) > 0.001)
                throw new InvalidOperationException("縦線が再生位置を示していません。");
            var originalX = qualityGraphPanel.PlayheadX;
            ApplySeek(TimeSpan.FromSeconds(0.1));
            await comparisonChangeTask;
            UpdateTimeDisplay();
            if (qualityGraphPanel.PlayheadX >= originalX) throw new InvalidOperationException("シークバー操作に縦線が追従しません。");
            var beforePlay = qualityGraphPanel.PlayheadPosition;
            // 定期タイマーを止めても映像フレームの通知で表示が進むことを確認する。
            displayTimer.Stop();
            Play_Click(this, new RoutedEventArgs());
            await Task.Delay(250);
            if (qualityGraphPanel.PlayheadPosition <= beforePlay ||
                Math.Abs(SeekSlider.Value - qualityGraphPanel.PlayheadPosition.TotalSeconds) > 0.001)
                throw new InvalidOperationException("定期タイマー停止中にフレーム通知で再生表示が更新されません。");
            PausePlayback();
            displayTimer.Start();
            if (qualityGraphPanel.PlayheadPosition <= beforePlay) throw new InvalidOperationException("再生中に縦線が進みません。");
            foreach (var playing in new[] { false, true })
            {
                if (playing) Play_Click(this, new RoutedEventArgs());
                qualityGraphPanel.BeginPlayheadDrag(0.25);
                var requested = qualityGraphPanel.PlayheadPosition;
                qualityGraphPanel.SetPlaybackPosition(TimeSpan.Zero);
                await Task.Delay(250);
                UpdateTimeDisplay();
                if (qualityGraphPanel.PlayheadPosition != requested || Math.Abs(SeekSlider.Value - requested.TotalSeconds) > 0.001)
                    throw new InvalidOperationException("ドラッグ中に縦線が戻るか、シークバーが連動しません。");
                qualityGraphPanel.MovePlayheadDrag(0.75);
                requested = qualityGraphPanel.PlayheadPosition;
                qualityGraphPanel.EndPlayheadDrag();
                await comparisonChangeTask;
                if (Math.Abs(CurrentPosition().TotalSeconds - requested.TotalSeconds) > 0.05 ||
                    comparisonPanel?.IsPlaying != playing)
                    throw new InvalidOperationException("縦線の指定位置または再生状態が反映されません。");
                PausePlayback();
            }
            Console.WriteLine("再生位置の縦線: 再生・シーク追従、ドラッグ中の位置保持、バー連動、確定シーク、再生状態維持が合格");
            UpdateLayout();
            var snapshot = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight,
                96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            snapshot.Render((System.Windows.Media.Visual)Content);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(snapshot));
            var layoutPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,
                "../../../../../measurement-results/main-quality-graphs-layout.png"));
            using (var stream = System.IO.File.Create(layoutPath)) encoder.Save(stream);
            qualityResults.Clear();
            RefreshQualityValues();
            if (qualityGraphPanel.FilePointCount != 0 || qualityGraphPanel.DisplayedVideoIds.Count != 0)
                throw new InvalidOperationException("リセット後のグラフが残っています。");
            Console.WriteLine("主画面グラフ: 配置・評価結果反映・一覧と点の選択連動・シーク・リセットが合格");
            Application.Current.Shutdown(0);
        }
        catch (Exception exception) { Console.Error.WriteLine(exception.Message); Application.Current.Shutdown(1); }
    }

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
            var buttonLabels = PlaybackButtons.Children.OfType<System.Windows.Controls.Button>()
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
