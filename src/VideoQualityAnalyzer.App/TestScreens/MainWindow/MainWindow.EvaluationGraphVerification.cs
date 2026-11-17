using System.Windows;
using VideoQualityAnalyzer.Media;
using VideoQualityAnalyzer.Core;

namespace VideoQualityAnalyzer.App;

public partial class MainWindow
{
    private async Task VerifyMainEvaluationAsync()
    {
        try
        {
            var candidate = videos.First(entry => !entry.IsReference);
            VideoList.SelectedItem = candidate;
            await comparisonChangeTask;
            if (!evaluationMetrics.SequenceEqual([QualityMetric.Ssim]) || FindName("VmafCheck") is not null ||
                FindName("SsimCheck") is not null || FindName("PsnrCheck") is not null || FindName("EvaluationHelpBorder") is not null ||
                FindName("EvaluationRangeText") is not null)
                throw new InvalidOperationException("指標の初期値または旧評価UIの削除が不正です。");
            StartEvaluationForVerification(EvaluationSelection.Full, [QualityMetric.Ssim]);
            if (evaluationCancellation is null || !Equals(EvaluationRangeButton.Content, "評価を中止") ||
                AddVideoButton.IsEnabled || SetReferenceButton.IsEnabled || RemoveVideoButton.IsEnabled || Application.Current.Windows.Count != 1)
                throw new InvalidOperationException("主画面での評価開始状態が不正です。");
            await evaluationTask;
            var key = (candidate.Descriptor.Id, QualityMetric.Ssim);
            if (!qualityResults.TryGetValue(key, out var completed) || completed.Frames.Count != 30 ||
                !Equals(EvaluationRangeButton.Content, "評価を開始") || !EvaluationRangeButton.IsEnabled)
                throw new InvalidOperationException("主画面で評価結果が確定されません。");
            qualityGraphPanel!.SelectMetric(QualityMetric.Ssim);
            if (qualityGraphPanel.FilePointCount != 1) throw new InvalidOperationException("評価結果が主画面グラフに反映されません。");
            qualityGraphPanel.VerifyScreenPalette();
            if (PreviewLeftSelect.SelectedItem is not VideoEntry left ||
                PreviewRightSelect.SelectedItem is not VideoEntry right ||
                PreviewLeftSelect.Text != left.Descriptor.Name || PreviewRightSelect.Text != right.Descriptor.Name)
                throw new InvalidOperationException("暗色選択欄に動画名が表示されません。");
            StartEvaluationForVerification(EvaluationSelection.Full, [QualityMetric.Ssim]);
            await evaluationTask;
            if (!ReferenceEquals(completed, qualityResults[key])) throw new InvalidOperationException("完了済み結果が再計算されました。");
            StartEvaluationForVerification(EvaluationSelection.Full, [QualityMetric.Ssim, QualityMetric.Psnr]);
            if (evaluationCancellation is null) throw new InvalidOperationException("中止試験の評価が始まりません。");
            EvaluationRangeButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            await evaluationTask;
            if (qualityResults.ContainsKey((candidate.Descriptor.Id, QualityMetric.Psnr)) ||
                !ReferenceEquals(completed, qualityResults[key]) || !EvaluationRangeButton.IsEnabled ||
                !Equals(EvaluationRangeButton.Content, "評価を開始"))
                throw new InvalidOperationException("評価中止後の結果またはボタン状態が不正です。");
            var other = videos.FirstOrDefault(entry => !entry.IsReference && entry != candidate);
            if (other is not null)
            {
                VideoList.SelectedItem = other;
                await comparisonChangeTask;
                StartEvaluationForVerification(EvaluationSelection.Full, [QualityMetric.Psnr]);
                await evaluationTask;
                if (!qualityResults.ContainsKey((other.Descriptor.Id, QualityMetric.Psnr)) ||
                    qualityResults.ContainsKey((candidate.Descriptor.Id, QualityMetric.Psnr)) ||
                    VideoList.SelectedItem != other)
                    throw new InvalidOperationException("主画面で選んだ動画が設定画面の評価対象に反映されません。");
            }
            UpdateLayout();
#pragma warning disable WPF0001 // .NET 10のFluentテーマは実験的API。
            var selectedMode = DisplayStyleSettings.ActiveMode switch
            {
                "None" => ThemeMode.None, "System" => ThemeMode.System,
                "Light" => ThemeMode.Light, _ => ThemeMode.Dark
            };
            if (!Application.Current.ThemeMode.Equals(selectedMode) ||
#pragma warning restore WPF0001
                FontFamily.Source != "Yu Gothic UI" || FontSize != (double)FindResource("UiFontMedium"))
                throw new InvalidOperationException("表示スタイルまたは共通フォントが不正です。");
            if (Content is not System.Windows.Controls.DockPanel root ||
                Background is not System.Windows.Media.SolidColorBrush windowBackground ||
                root.Background is not System.Windows.Media.SolidColorBrush rootBackground ||
                Foreground is not System.Windows.Media.SolidColorBrush foreground ||
                windowBackground.Color != ((System.Windows.Media.SolidColorBrush)FindResource("BackgroundBrush")).Color ||
                rootBackground.Color != windowBackground.Color ||
                foreground.Color != ((System.Windows.Media.SolidColorBrush)FindResource("TextBrush")).Color)
                throw new InvalidOperationException("主画面の余白または標準文字色が共通パレットと一致しません。");
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
            var originalWindowHeight = Height;
            var originalRatio = PreviewRow.ActualHeight / QualityGraphRow.ActualHeight;
            Height += 100;
            UpdateLayout();
            if (Math.Abs(PreviewRow.ActualHeight / QualityGraphRow.ActualHeight - originalRatio) > 0.01)
                throw new InvalidOperationException("ウィンドウ変更時にプレビューとグラフの比率が保たれません。");
            Height = originalWindowHeight;
            UpdateLayout();
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

}
