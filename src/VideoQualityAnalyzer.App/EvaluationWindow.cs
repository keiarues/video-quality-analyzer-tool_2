using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

// 品質評価を独立した画面で扱い、一覧やプレビューの処理から分離する。
public sealed class EvaluationWindow : Window
{
    private readonly VideoDescriptor reference;
    private readonly VideoDescriptor[] candidates;
    private readonly IDictionary<(string VideoId, QualityMetric Metric), QualityResult> results;
    private readonly Dictionary<QualityMetric, CheckBox> metrics = [];
    private readonly ObservableCollection<ResultRow> rows = [];
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private readonly Button start = new() { Content = "評価を開始" };
    private readonly Button reset = new() { Content = "評価結果をリセット" };
    private CancellationTokenSource? cancellation;
    private bool running;
    private readonly bool verify;

    public EvaluationWindow(VideoDescriptor reference, VideoDescriptor[] candidates,
        IDictionary<(string VideoId, QualityMetric Metric), QualityResult> results, bool verify = false,
        Action<TimeSpan>? seek = null, Action<string>? select = null,
        VideoDescriptor[]? allVideos = null, IReadOnlyDictionary<string, string>? groups = null)
    {
        this.verify = verify;
        this.reference = reference;
        this.candidates = candidates;
        this.results = results;
        Title = "品質評価";
        Width = 850;
        Height = 520;
        var panel = new DockPanel { Margin = new Thickness(12) };
        Content = panel;
        var header = new TextBlock { Text = $"元動画: {reference.Name}\n対象: {candidates.Length}本（一覧で選択した比較動画）",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        var buttons = new WrapPanel();
        DockPanel.SetDock(buttons, Dock.Top);
        panel.Children.Add(buttons);
        foreach (var metric in Enum.GetValues<QualityMetric>())
        {
            var check = new CheckBox { Content = metric.ToString().ToUpperInvariant(),
                IsChecked = metric == QualityMetric.Ssim, Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Center };
            check.ToolTip = new TextBlock { MaxWidth = 380, TextWrapping = TextWrapping.Wrap, Text = metric switch
            {
                QualityMetric.Vmaf => "VMAF：複数の特徴から、人が感じる映像品質を推定する指標です。一般に0～100で、大きいほど元動画に近い品質と評価します。libvmaf対応のFFmpegが必要です。",
                QualityMetric.Ssim => "SSIM：元動画と比較動画の明るさ・コントラスト・構造の類似度を評価します。1に近いほど似ています。",
                _ => "PSNR：元動画との画素の誤差を、dB（デシベル）で表します。大きいほど誤差が小さいことを示します。完全一致による無限大は現在の平均集計から除外します。"
            } };
            metrics.Add(metric, check);
            buttons.Children.Add(check);
        }
        buttons.Children.Add(start);
        var cancel = new Button { Content = "中断" };
        buttons.Children.Add(cancel);
        buttons.Children.Add(reset);
        var graphs = new Button { Content = "グラフ" };
        buttons.Children.Add(graphs);
        graphs.Click += (_, _) => new QualityGraphWindow(candidates, results, seek, select, allVideos: allVideos, groups: groups) { Owner = this }.ShowDialog();
        foreach (var frameCsv in new[] { false, true })
        {
            var export = new Button { Content = frameCsv ? "フレームCSV" : "動画CSV" };
            buttons.Children.Add(export);
            export.Click += async (_, _) =>
            {
                var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "CSV|*.csv",
                    FileName = frameCsv ? "frames.csv" : "videos.csv" };
                if (dialog.ShowDialog(this) != true) return;
                try
                {
                    var text = frameCsv ? QualityCsvExporter.Frames(candidates, results)
                        : QualityCsvExporter.Summary(candidates, results);
                    await System.IO.File.WriteAllTextAsync(dialog.FileName, text, new System.Text.UTF8Encoding(true));
                    status.Text = "CSVを保存しました。";
                }
                catch (Exception exception) { status.Text = $"CSVを保存できません: {exception.Message}"; }
            };
        }
        start.Click += async (_, _) => await EvaluateSelectedAsync();
        cancel.Click += (_, _) => cancellation?.Cancel();
        reset.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "すべての評価結果を削除しますか？", "評価結果", MessageBoxButton.YesNo)
                != MessageBoxResult.Yes) return;
            results.Clear();
            RefreshRows();
        };
        DockPanel.SetDock(status, Dock.Bottom);
        panel.Children.Add(status);
        panel.Children.Add(new DataGrid { ItemsSource = rows, IsReadOnly = true, AutoGenerateColumns = false,
            Columns =
            {
                new DataGridTextColumn { Header = "動画", Binding = new System.Windows.Data.Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) },
                new DataGridTextColumn { Header = "指標", Binding = new System.Windows.Data.Binding("Metric") },
                new DataGridTextColumn { Header = "平均値", Binding = new System.Windows.Data.Binding("Average") { StringFormat = "F4" } },
                new DataGridTextColumn { Header = "集計フレーム数", Binding = new System.Windows.Data.Binding("Frames") }
            } });
        RefreshRows();
        status.Text = "完了済みの組み合わせは再計算しません。VMAFにはlibvmaf対応FFmpegが必要です。";
        Closing += (_, e) =>
        {
            if (!running) return;
            e.Cancel = true;
            cancellation?.Cancel();
            status.Text = "評価を中断中です。終了後に画面を閉じてください。";
        };
        if (verify) Loaded += async (_, _) =>
        {
            try
            {
                if (metrics.Any(pair => pair.Value.IsChecked != (pair.Key == QualityMetric.Ssim)) ||
                    metrics.Values.Any(check => check.ToolTip is not TextBlock { Text.Length: > 20 }))
                    throw new InvalidOperationException("初期指標または説明が不正です。");
                metrics[QualityMetric.Vmaf].IsChecked = false;
                metrics[QualityMetric.Ssim].IsChecked = metrics[QualityMetric.Psnr].IsChecked = true;
                await EvaluateSelectedAsync();
                if (results.Count != candidates.Length * 2) throw new InvalidOperationException("完了結果が不足しています。");
                var previous = results.ToDictionary(pair => pair.Key, pair => pair.Value);
                await EvaluateSelectedAsync();
                if (previous.Any(pair => !ReferenceEquals(pair.Value, results[pair.Key])))
                    throw new InvalidOperationException("完了結果が再計算されました。");
                new QualityGraphWindow(candidates, results, verify: true) { Owner = this }.Show();
            }
            catch (Exception exception) { Console.Error.WriteLine(exception); Application.Current.Shutdown(1); }
        };
    }

    private async Task EvaluateSelectedAsync()
    {
        if (running) return;
        var selected = metrics.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key).ToArray();
        if (selected.Length == 0) { status.Text = "評価指標を選択してください。"; return; }
        var executable = FfmpegRgbReader.FindExecutable();
        if (executable is null) { status.Text = "FFmpegが見つかりません。"; return; }
        running = true;
        start.IsEnabled = reset.IsEnabled = false;
        using var source = new CancellationTokenSource();
        cancellation = source;
        var failed = 0;
        try
        {
            var evaluator = new QualityEvaluationService(executable);
            foreach (var candidate in candidates)
            foreach (var metric in selected)
            {
                source.Token.ThrowIfCancellationRequested();
                var key = (candidate.Id, metric);
                if (results.ContainsKey(key)) continue;
                status.Text = $"{candidate.Name} / {metric} を評価中…";
                try
                {
                    var progress = new Progress<TimeSpan>(position =>
                        status.Text = $"{candidate.Name} / {metric}: {position.TotalSeconds:F1}秒を処理済み");
                    var result = await evaluator.EvaluateAsync(reference, candidate, metric, progress, source.Token);
                    results[key] = result;
                    RefreshRows();
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception)
                {
                    failed++;
                    status.Text = exception.Message;
                    if (!verify) MessageBox.Show(this, $"{candidate.Name} / {metric}\n{exception.Message}", "評価できません");
                }
            }
            status.Text = $"評価を終了しました。失敗 {failed}件。完了結果は保持しています。";
        }
        catch (OperationCanceledException) { status.Text = "中断しました。完了済みの結果は保持しています。"; }
        finally
        {
            cancellation = null;
            running = false;
            start.IsEnabled = reset.IsEnabled = true;
        }
    }

    private void RefreshRows()
    {
        rows.Clear();
        foreach (var candidate in candidates)
        foreach (var metric in Enum.GetValues<QualityMetric>())
            if (results.TryGetValue((candidate.Id, metric), out var result))
                rows.Add(new ResultRow(candidate.Name, metric.ToString().ToUpperInvariant(), result.Average, result.Frames.Count));
    }

    private sealed record ResultRow(string Name, string Metric, double Average, int Frames);
}
