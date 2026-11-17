using System.IO;
using System.Text.Json;
using System.Windows;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public partial class MainWindow
{
    private async Task VerifyMainGraphPlaybackAsync(string cachePath)
    {
        var output = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(cachePath))!, "main-playback.json");
        try
        {
            var cache = await SsimTestCache.LoadAsync(cachePath);
            await AddFilesAsync(new[] { cache.Reference.Path }.Concat(cache.Entries.Select(e => e.Video.Path)));
            if (VideoList.SelectedItems.Count != 0 || ReferenceList.SelectedItems.Count != 0)
                throw new InvalidOperationException("動画読み込み後に一覧の選択が残っています。");
            foreach (var entry in cache.Entries)
            {
                var registered = videos.Single(v => string.Equals(v.Descriptor.Path, entry.Video.Path, StringComparison.OrdinalIgnoreCase));
                qualityResults[(registered.Descriptor.Id, QualityMetric.Ssim)] = entry.Result;
            }
            RefreshQualityValues();
            VideoList.SelectedItems.Clear();
            foreach (var entry in videos.Where(e => !e.IsReference)) VideoList.SelectedItems.Add(entry);
            await comparisonChangeTask;
            if (!comparisonActive || comparisonPanel is null || !comparisonPanel.IsReady || qualityGraphPanel is null)
                throw new InvalidOperationException("左右比較の準備に失敗しました。");
            qualityGraphPanel.SelectMetric(QualityMetric.Ssim);
            UpdateLayout();
            if (qualityGraphPanel.DisplayedVideoIds.Count != cache.Entries.Length)
                throw new InvalidOperationException("全比較動画のグラフが表示されていません。");
            qualityGraphPanel.VerifyCachedGraphPixels();
            Activate();
            var rows = new List<object>();
            foreach (var mode in new[] { 0, 1 })
            {
                PausePlayback(); comparisonPanel.SelectMode(mode);
                ApplySeek(TimeSpan.Zero);
                await comparisonChangeTask;
                await Task.Delay(500);
                var observed = new HashSet<long>();
                void OnFrame()
                {
                    var position = comparisonPanel.Position.TotalSeconds;
                    if (position >= 2 && position < 32)
                        observed.Add((long)Math.Round(position * cache.Reference.AverageFrameRate));
                }
                comparisonPanel.FrameShown += OnFrame;
                try
                {
                    Play_Click(this, new RoutedEventArgs());
                    await Task.Delay(TimeSpan.FromSeconds(35));
                    PausePlayback();
                }
                finally { comparisonPanel.FrameShown -= OnFrame; }
                var expected = (int)(Math.Ceiling(32 * cache.Reference.AverageFrameRate - .000001) - Math.Ceiling(2 * cache.Reference.AverageFrameRate - .000001));
                rows.Add(new { Mode = mode == 0 ? "2画面" : "ワイプ", Graphs = qualityGraphPanel.DisplayedVideoIds.Count,
                    Expected = expected, Reflected = observed.Count, MissingPercent = 100.0 * Math.Max(0, expected - observed.Count) / expected });
            }
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { Note = "保存済みSSIM。左右比較画像の反映通知を観測。実画面提示fpsは未測定。各モード1回。", Rows = rows }, new JsonSerializerOptions { WriteIndented = true }));
            Application.Current.Shutdown(0);
        }
        catch (Exception error)
        {
            await File.WriteAllTextAsync(output + ".error.txt", error.ToString());
            Application.Current.Shutdown(1);
        }
    }
}
