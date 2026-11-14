using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public sealed class PartialEvaluationTestWindow : Window
{
    private readonly StackPanel settings = new();
    private readonly TextBox referencePath = new(), candidatePath = new();
    private readonly TextBox startSeconds = new() { Text = "0" }, endSeconds = new() { Text = "10" };
    private readonly TextBox period = new() { Text = "60" }, periodicLength = new() { Text = "10" };
    private readonly TextBox count = new() { Text = "6" }, uniformLength = new() { Text = "10" };
    private readonly Dictionary<PartialEvaluationMode, RadioButton> modes = [];
    private readonly Dictionary<QualityMetric, CheckBox> metrics = [];
    private readonly TextBox output = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 160 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) };
    private readonly Button preview = new() { Content = "対象区間を確認" }, execute = new() { Content = "部分評価を実行" };
    private CancellationTokenSource? cancellation;

    public PartialEvaluationTestWindow(bool automatic = false, string[]? paths = null)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "部分品質評価テスト";
        Width = 880; Height = 760; MinWidth = 650; MinHeight = 550;
        var root = new DockPanel { Margin = new Thickness(16) };
        Content = root;
        DockPanel.SetDock(settings, Dock.Top); root.Children.Add(settings);
        AddFile("元動画", referencePath); AddFile("比較動画", candidatePath);
        if (paths is { Length: >= 2 }) { referencePath.Text = paths[0]; candidatePath.Text = paths[1]; }
        AddMode(PartialEvaluationMode.TimeRange, "時間範囲指定", [("開始（秒）", startSeconds), ("終了（秒・含まない）", endSeconds)]);
        AddMode(PartialEvaluationMode.Periodic, "周期的な区間指定", [("周期（秒）", period), ("各周期の先頭から（秒）", periodicLength)]);
        AddMode(PartialEvaluationMode.Uniform, "全体に均等配置", [("区間数", count), ("各区間の長さ（秒）", uniformLength)]);
        modes[PartialEvaluationMode.TimeRange].IsChecked = true;
        settings.Children.Add(new TextBlock { Text = "均等配置：先頭・末尾を含めて等間隔（1区間の場合は中央）。周期指定の末尾は動画末尾で切ります。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) });
        var metricPanel = new WrapPanel(); settings.Children.Add(metricPanel);
        foreach (var metric in Enum.GetValues<QualityMetric>())
        {
            var check = new CheckBox { Content = metric.ToString().ToUpperInvariant(), IsChecked = metric == QualityMetric.Ssim,
                Margin = new Thickness(8), ToolTip = QualityMetricHelp.GetText(metric) };
            metrics.Add(metric, check); metricPanel.Children.Add(check);
        }
        var actions = new WrapPanel(); DockPanel.SetDock(actions, Dock.Top); root.Children.Add(actions);
        actions.Children.Add(preview); actions.Children.Add(execute);
        var cancel = new Button { Content = "中断" }; actions.Children.Add(cancel);
        cancel.Click += (_, _) => cancellation?.Cancel();
        preview.Click += async (_, _) => await RunAsync(false);
        execute.Click += async (_, _) => await RunAsync(true);
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status); root.Children.Add(output);
        status.Text = "CFR・同解像度・同fps用。平均値は評価対象のみ。区間外の復号も必要なため、計算時間の短縮率は未測定です。";
        Closing += (_, e) => { if (cancellation is not null) { e.Cancel = true; cancellation.Cancel(); } };
        if (automatic) Loaded += async (_, _) =>
        {
            try
            {
                UpdateLayout();
                var image = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                image.Render(this);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                var imagePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,
                    "../../../../../measurement-results/partial-evaluation/screen.png"));
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(imagePath)!);
                using (var stream = System.IO.File.Create(imagePath)) encoder.Save(stream);
                foreach (var mode in Enum.GetValues<PartialEvaluationMode>())
                {
                    modes[mode].IsChecked = true;
                    if (modes.Count(pair => pair.Value.IsChecked == true) != 1)
                        throw new InvalidOperationException("評価方式が排他選択になっていません。");
                    endSeconds.Text = "1"; period.Text = "1"; periodicLength.Text = "0.25";
                    count.Text = "2"; uniformLength.Text = "0.25";
                    await RunAsync(true, true);
                }
                Application.Current.Shutdown(0);
            }
            catch (Exception error) { Console.Error.WriteLine(error); Application.Current.Shutdown(1); }
        };
    }

    private void AddFile(string label, TextBox path)
    {
        var row = new DockPanel { Margin = new Thickness(4) };
        row.Children.Add(new TextBlock { Text = label, Width = 70, VerticalAlignment = VerticalAlignment.Center });
        var browse = new Button { Content = "選択" }; DockPanel.SetDock(browse, Dock.Right); row.Children.Add(browse);
        browse.Click += (_, _) => { var dialog = new OpenFileDialog { Filter = "動画ファイル|*.*" }; if (dialog.ShowDialog(this) == true) path.Text = dialog.FileName; };
        row.Children.Add(path); settings.Children.Add(row);
    }

    private void AddMode(PartialEvaluationMode mode, string title, (string Label, TextBox Input)[] fields)
    {
        var group = new StackPanel { Margin = new Thickness(8) };
        var radio = new RadioButton { Content = title, GroupName = "部分評価方式", Foreground = (System.Windows.Media.Brush)FindResource("TextBrush") };
        modes.Add(mode, radio); group.Children.Add(radio);
        var inputs = new WrapPanel { Margin = new Thickness(20, 6, 0, 0), IsEnabled = false };
        radio.Checked += (_, _) => inputs.IsEnabled = true;
        radio.Unchecked += (_, _) => inputs.IsEnabled = false;
        foreach (var field in fields)
        {
            inputs.Children.Add(new TextBlock { Text = field.Label, Margin = new Thickness(4), VerticalAlignment = VerticalAlignment.Center });
            field.Input.Width = 85; field.Input.Margin = new Thickness(4); inputs.Children.Add(field.Input);
        }
        group.Children.Add(inputs); settings.Children.Add(group);
    }

    private async Task RunAsync(bool evaluate, bool verify = false)
    {
        if (cancellation is not null) return;
        using var source = new CancellationTokenSource(); cancellation = source;
        settings.IsEnabled = preview.IsEnabled = execute.IsEnabled = false;
        try
        {
            var probe = new FfprobeService(FfprobeService.FindExecutable() ?? throw new InvalidOperationException("FFprobeが見つかりません。"));
            var reference = await probe.ProbeAsync(referencePath.Text, source.Token);
            var candidate = await probe.ProbeAsync(candidatePath.Text, source.Token);
            var mode = modes.Single(pair => pair.Value.IsChecked == true).Key;
            double Read(TextBox input) => double.TryParse(input.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value)
                && double.IsFinite(value) ? value : throw new ArgumentException("設定には有効な数値を入力してください。");
            var ranges = PartialEvaluationPlan.Create(reference, mode,
                mode == PartialEvaluationMode.TimeRange ? Read(startSeconds) : 0,
                mode == PartialEvaluationMode.TimeRange ? Read(endSeconds) : 0,
                mode == PartialEvaluationMode.Periodic ? Read(period) : 0,
                mode == PartialEvaluationMode.Periodic ? Read(periodicLength) : mode == PartialEvaluationMode.Uniform ? Read(uniformLength) : 0,
                mode == PartialEvaluationMode.Uniform ? int.TryParse(count.Text, out var n) ? n : throw new ArgumentException("区間数には整数を入力してください。") : 0);
            var targetFrames = ranges.Sum(range => range.End - range.Start);
            output.Text = $"方式：{modes[mode].Content}\n対象：{targetFrames}フレーム / {ranges.Count}区間\n" +
                string.Join("\n", ranges.Select((range, i) => $"{i + 1}: {range.Start / reference.AverageFrameRate:F3}～{range.End / reference.AverageFrameRate:F3}秒 / フレーム {range.Start}～{range.End - 1}"));
            if (!evaluate) { status.Text = "対象区間を確認しました。終了時刻・終了フレームは含みません。"; return; }
            var selected = metrics.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key).ToArray();
            if (selected.Length == 0) throw new ArgumentException("評価指標を選択してください。");
            var service = new QualityEvaluationService(FfmpegRgbReader.FindExecutable() ?? throw new InvalidOperationException("FFmpegが見つかりません。"));
            foreach (var metric in selected)
            {
                var progress = new Progress<TimeSpan>(time => { if (cancellation == source) status.Text = $"{metric}: 評価対象の{time.TotalSeconds:F1}秒を処理中…"; });
                var result = await service.EvaluatePartialAsync(reference, candidate, metric, ranges, progress, source.Token);
                if (verify && (result.Frames.Count != targetFrames ||
                    !result.Frames.Select(frame => frame.Number).SequenceEqual(ranges.SelectMany(range => Enumerable.Range(0, checked((int)(range.End - range.Start))).Select(offset => range.Start + offset))) ||
                    result.Average < 0.999))
                    throw new InvalidOperationException("評価フレーム数・元番号・同一動画のSSIMが不正です。");
                output.AppendText($"\n{metric}: 評価対象平均 {result.Average:F6} / 集計 {result.Frames.Count}フレーム");
            }
            status.Text = "部分評価が完了しました。結果はこのテスト画面内のみで表示します。";
        }
        catch (OperationCanceledException) { status.Text = "中断しました。途中結果は完了結果として扱いません。"; if (verify) throw; }
        catch (Exception error) { status.Text = error.Message; if (verify) throw; }
        finally { cancellation = null; settings.IsEnabled = preview.IsEnabled = execute.IsEnabled = true; }
    }
}
