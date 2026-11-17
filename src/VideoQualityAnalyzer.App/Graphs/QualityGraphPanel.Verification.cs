using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public sealed partial class QualityGraphPanel
{
    private void VerifyGraphLayout(Grid grid, Border legendBox, ScrollViewer legendScroll,
        System.Windows.Controls.Primitives.Thumb legendDrag, Button collapseLegend, Button minimizeFrame,
        Button minimizeFile, QualityGraphPaneController paneController, Action clearFileSelection, Action? clearSelection)
    {
        metric.SelectedIndex = 1;
        Draw();
        try
        {
            UpdateLayout();
            if (metric.Items.Count != 3 || grid.ColumnDefinitions.Count != 3 ||
                !timeline.Children.OfType<Polyline>().Any(line => line.Tag is null) || !averages.Children.OfType<Ellipse>().Any())
                throw new InvalidOperationException("グラフのタブまたは横並び表示が不正です。");
            foreach (var canvas in new[] { timeline, averages })
            {
                if (canvas.Children.OfType<Line>().Count(line => Equals(line.Tag, "グリッド")) != 12 ||
                    canvas.Children.OfType<TextBlock>().Count(label => Equals(label.Tag, "目盛り")) != 12)
                    throw new InvalidOperationException("軸の目盛りまたはグリッドが不足しています。");
                if (!canvas.Children.OfType<TextBlock>().Any(label => Equals(label.Tag, "縦軸名") &&
                        label.Text == "SSIM" && label.RenderTransform is RotateTransform { Angle: -90 }) ||
                    !canvas.Children.OfType<TextBlock>().Any(label => Equals(label.Tag, "品質方向") && label.Text == "↑ 高画質"))
                    throw new InvalidOperationException("縦軸名または品質方向の案内がありません。");
            }
            // 外れたフレーム値に平均値の軸が引っ張られず、単一値でも描画できる。
            var frameRange = QualityGraphScale.VerticalRange([0.1, 0.99]);
            var averageRange = QualityGraphScale.VerticalRange([0.94, 0.95]);
            var singleRange = QualityGraphScale.VerticalRange([0.95]);
            if (averageRange.Maximum - averageRange.Minimum >= frameRange.Maximum - frameRange.Minimum ||
                singleRange.Minimum >= 0.95 || singleRange.Maximum <= 0.95 ||
                QualityGraphScale.VerticalRange([]) != (0d, 1d))
                throw new InvalidOperationException("グラフ別の縦軸範囲または単一値・空データの範囲が不正です。");
            if (averages.Children.OfType<TextBlock>().Count(label => Equals(label.Tag, "点の評価値")) != FilePointCount ||
                legend.Children.Count != FilePointCount)
                throw new InvalidOperationException("点の評価値・ビットレート表示または凡例が不足しています。");
            // 凡例の現在位置に点を追加し、内容更新時に別の端へ退避することを確認する。
            legendController.Place();
            var originalLegend = new Point(Canvas.GetLeft(legendBox), Canvas.GetTop(legendBox));
            var obstacle = new Ellipse { Width = 12, Height = 12 };
            Canvas.SetLeft(obstacle, originalLegend.X + legendBox.DesiredSize.Width / 2);
            Canvas.SetTop(obstacle, originalLegend.Y + legendBox.DesiredSize.Height / 2);
            averages.Children.Add(obstacle);
            legendController.Place();
            if (new Point(Canvas.GetLeft(legendBox), Canvas.GetTop(legendBox)) == originalLegend)
                throw new InvalidOperationException("凡例が追加された点を避けて移動しません。");
            averages.Children.Remove(obstacle);
            legendController.Place();
            if (legend.Children.OfType<StackPanel>().Any(item =>
                !item.Children.OfType<TextBlock>().Any(text => text.Text.Contains("kbps"))))
                throw new InvalidOperationException("凡例に評価値とビットレートがありません。");
            collapseLegend.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (legendScroll.Visibility != Visibility.Collapsed) throw new InvalidOperationException("凡例を折りたためません。");
            collapseLegend.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            legendDrag.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(-20, 10)
                { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragDeltaEvent });
            if (legendScroll.Visibility != Visibility.Visible || Canvas.GetLeft(legendBox) < 0 || Canvas.GetTop(legendBox) < 0)
                throw new InvalidOperationException("凡例の復元または移動が不正です。");
            // 別の動画が表示対象だった状態からファイル点を選択して切り替える。
            var target = averages.Children.OfType<Ellipse>().First();
            selectedIds.Clear();
            selectedIds.Add("別の動画");
            target.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,
                Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = MouseLeftButtonDownEvent });
            if (timeline.Children.OfType<Polyline>().Count(line => line.Tag is null) != 1 || selectedIds.Single() != (string)target.Tag)
                throw new InvalidOperationException("ファイル選択がフレームグラフに反映されません。");
            metric.SelectedIndex = 2;
            UpdateLayout();
            if (!timeline.Children.OfType<Polyline>().Any(line => line.Tag is null)) throw new InvalidOperationException("PSNRタブへ切り替わりません。");
            var playbackVideo = (allVideos ?? videos).First(video => playbackResults.ContainsKey((video.Id, QualityMetric.Psnr)));
            SetPlaybackSources(playbackVideo with { Id = "検証用元動画", Path = "元動画.mkv" }, playbackVideo, "検証用元動画");
            SetPlaybackPosition(TimeSpan.FromSeconds(playbackResults[(playbackVideo.Id, QualityMetric.Psnr)].Frames[0].Number / playbackVideo.AverageFrameRate));
            if (!leftPlaybackStatus.Text.StartsWith("--\n") || rightPlaybackStatus.Text.StartsWith("--\n") ||
                !rightPlaybackStatus.Text.Contains(playbackVideo.Name) || !playbackTimeLabel.Text.EndsWith("秒"))
                throw new InvalidOperationException("左右の再生ステータスまたは元動画の評価値表示が不正です。");
            if (!Equals(rightPlaybackStatus.Foreground, videoColors[playbackVideo.Id]) ||
                !Equals(leftPlaybackStatus.Foreground, UiTheme.OnLight) || !Equals(playbackTimeLabel.Foreground, UiTheme.OnLight))
                throw new InvalidOperationException("再生ステータスの系列色または秒数の標準色が不正です。");
            UpdateLayout();
            var legendTarget = legend.Children.OfType<StackPanel>().First();
            legendTarget.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,
                Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = MouseLeftButtonDownEvent });
            if (selectedIds.Count != 1) throw new InvalidOperationException("凡例から動画を選択できません。");
            clearFileSelection();
            if (clearSelection is null && selectedIds.Count != 0) throw new InvalidOperationException("グラフの選択を解除できません。");
            foreach (var toggle in new[] { minimizeFrame, minimizeFile })
            {
                var other = toggle == minimizeFrame ? minimizeFile : minimizeFrame;
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (toggle.Visibility != Visibility.Collapsed || other.Visibility != Visibility.Visible)
                    throw new InvalidOperationException("最小化したグラフのボタン表示が不正です。");
                other.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (paneController.MinimizedColumn != -1 || toggle.Visibility != Visibility.Visible || other.Visibility != Visibility.Visible ||
                    grid.ColumnDefinitions[0].Width != paneController.SavedLeft || grid.ColumnDefinitions[2].Width != paneController.SavedRight)
                    throw new InvalidOperationException("反対側のボタンでグラフのサイズを復元できません。");
            }
            UpdateLayout();
            foreach (var ranges in new EvaluationFrameRange[][]
            {
                [new(3, 9)],
                [new(0, 3), new(15, 18)],
                [new(0, 3), new(27, 30)]
            })
            {
                foreach (var processed in new[] { TimeSpan.Zero, TimeSpan.FromSeconds(0.1), TimeSpan.FromSeconds(0.15) })
                {
                    var sourceTime = PartialEvaluationProgress.ToSourceTime(ranges, 30, processed);
                    SetEvaluationProgress(playbackVideo, QualityMetric.Ssim, sourceTime);
                    SetPlaybackPosition(TimeSpan.Zero);
                    if (Math.Abs(PlayheadPosition.TotalSeconds - sourceTime.TotalSeconds) > 0.000001)
                        throw new InvalidOperationException("部分評価の処理位置が元動画の時刻を示していません。");
                }
            }
            SetEvaluationProgress(playbackVideo, QualityMetric.Ssim, TimeSpan.FromSeconds(playbackVideo.Duration.TotalSeconds / 2));
            var progressPosition = PlayheadPosition;
            SetPlaybackPosition(TimeSpan.Zero);
            if (metric.SelectedIndex != (int)QualityMetric.Ssim || PlayheadPosition != progressPosition ||
                Math.Abs(progressPosition.TotalSeconds - playbackVideo.Duration.TotalSeconds / 2) > 0.001)
                throw new InvalidOperationException("計算中のタブまたは進捗位置が不正です。");
            EndEvaluationProgress(TimeSpan.Zero);
            SelectMetric(QualityMetric.Psnr);
            UpdateLayout();
            var snapshot = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
            snapshot.Render((Visual)Content);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(snapshot));
            var output = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,
                "../../../../../measurement-results/quality-graph-layout.png"));
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output)!);
            using (var stream = System.IO.File.Create(output)) encoder.Save(stream);
            Console.WriteLine("グラフ: 目盛り・グリッド・指標タブ・横並び・ファイル選択連動が合格");
            Application.Current.Shutdown(0);
        }
        catch (Exception exception) { Console.Error.WriteLine(exception.Message); Application.Current.Shutdown(1); }
    }
    // 専用操作試験用。キャッシュ表示と、その場で描き直した静的グラフを照合する。
    internal void VerifyCachedGraphPixels()
    {
        UpdateLayout();
        byte[] Capture()
        {
            var width = Math.Max(1, (int)timeline.ActualWidth);
            var height = Math.Max(1, (int)timeline.ActualHeight);
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(timeline);
            var pixels = new byte[width * height * 4];
            bitmap.CopyPixels(pixels, width * 4, 0);
            return pixels;
        }
        var mode = timeline.CacheMode;
        if (mode is not BitmapCache) throw new InvalidOperationException("静的グラフのキャッシュが有効ではありません。");
        var cached = Capture();
        try
        {
            timeline.CacheMode = null;
            var fresh = Capture();
            var difference = cached.Zip(fresh, (a, b) => Math.Abs(a - b)).Average();
            if (difference > 3) throw new InvalidOperationException($"キャッシュと再描画した画像が一致しません（平均差 {difference:F3}）。");
        }
        finally { timeline.CacheMode = mode; }
    }

}
