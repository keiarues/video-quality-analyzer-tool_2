using System.IO;
using System.Text.Json;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public sealed partial class InputSeekTestWindow
{
    private async Task RunProductionAutomaticAsync(string[] paths)
    {
        var probePath = FfprobeService.FindExecutable()!;
        var executable = Path.Combine(Path.GetDirectoryName(probePath)!, "ffmpeg.exe");
        var service = new ContinuousInputEvaluationService(executable, probePath);
        var reference = await Probe().ProbeAsync(paths[0], CancellationToken.None);
        var candidate = await Probe().ProbeAsync(paths[1], CancellationToken.None);
        var rows = new List<object>();
        var ranges = new EvaluationFrameRange[] { new(0, 3), new(29, 32), new(87, 90) };
        static double Compare(QualityResult expected, QualityResult actual)
        {
            if (!expected.Frames.Select(frame => frame.Number).SequenceEqual(actual.Frames.Select(frame => frame.Number)))
                throw new InvalidDataException("実評価APIの元番号が基準と一致しません。");
            var difference = expected.Frames.Zip(actual.Frames).Max(pair => Math.Abs(pair.First.Value - pair.Second.Value));
            if (difference > 0.000001) throw new InvalidDataException("実評価APIの評価値が基準と一致しません。");
            return difference;
        }
        foreach (var metric in Enum.GetValues<QualityMetric>())
        {
            var expected = (await service.CompareAsync(reference, candidate, ranges, metric, false, null, CancellationToken.None)).Seek.Result;
            var stages = new List<string>();
            var actual = await service.EvaluatePartialAsync(reference, candidate, ranges, metric,
                new InlineProgress<InputSeekProgress>(value => { lock (stages) stages.Add(value.Stage); }), CancellationToken.None);
            if (stages.Count == 0 || stages.Any(stage => stage != "連続入力シーク") || actual.Method != QualityEvaluationMethod.PartialContinuous || actual.PartialVersion != QualityResult.CurrentPartialVersion)
                throw new InvalidOperationException("実評価APIで余分な基準計算が動くか、評価方式の識別が不正です。");
            QualityEvaluationTiming? timing = null; var timingCalls = 0;
            var bridge = new QualityEvaluationService(executable, value => { timing = value; timingCalls++; });
            var progress = new List<TimeSpan>();
            var integrated = await bridge.EvaluatePartialAsync(reference, candidate, metric, ranges,
                new InlineProgress<TimeSpan>(value => { lock (progress) progress.Add(value); }), CancellationToken.None);
            if (integrated.Method != QualityEvaluationMethod.PartialContinuous || timingCalls != 1 || timing is null || timing.FfmpegElapsed <= TimeSpan.Zero ||
                progress.Count == 0 || progress.Last() != TimeSpan.FromSeconds(9 / reference.AverageFrameRate))
                throw new InvalidOperationException("通常サービスの実評価接続・計測・進捗が不正です。");
            rows.Add(new { Metric = metric.ToString(), Method = integrated.Method.ToString(), integrated.PartialVersion, Values = integrated.Frames.Count,
                DirectDifference = Compare(expected, actual), BridgeDifference = Compare(expected, integrated), BaselineWasNotRun = true,
                CpuSeconds = timing.FfmpegCpuTime.TotalSeconds });
            output.AppendText($"実評価接続 {metric}：9フレーム一致・比較処理なし\n");
        }
        var reference10 = await Probe().ProbeAsync(paths[2], CancellationToken.None);
        var candidate10 = await Probe().ProbeAsync(paths[3], CancellationToken.None);
        foreach (var metric in Enum.GetValues<QualityMetric>())
        {
            var evaluator = new QualityEvaluationService(executable);
            var expected = await evaluator.EvaluateAsync(reference10, candidate10, metric, null, CancellationToken.None);
            var actual = await evaluator.EvaluatePartialAsync(reference10, candidate10, metric, [new(0, 90)], null, CancellationToken.None);
            if (actual.Method != QualityEvaluationMethod.PartialSequential || actual.PartialVersion != QualityResult.CurrentPartialVersion)
                throw new InvalidOperationException("10bit素材の先頭復号への切り替えが不正です。");
            rows.Add(new { Metric = metric.ToString(), Method = actual.Method.ToString(), actual.PartialVersion, Values = actual.Frames.Count,
                Native10BitDifference = Compare(expected, actual) });
            output.AppendText($"10bit {metric}：90フレーム一致・元の形式で先頭復号\n");
        }
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { await service.EvaluatePartialAsync(reference, candidate, ranges, QualityMetric.Vmaf, null, canceled.Token); throw new InvalidOperationException("実評価の事前中止が無効です。"); }
        catch (OperationCanceledException) { }
        using var running = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var trigger = new InlineProgress<InputSeekProgress>(value => { if (value.CompletedFrames > 0) running.Cancel(); });
        try { await service.EvaluatePartialAsync(reference, candidate, ranges, QualityMetric.Vmaf, trigger, running.Token); throw new InvalidOperationException("実評価の処理中止が無効です。"); }
        catch (OperationCanceledException) { }
        using var fallbackStop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var fallbackTrigger = new InlineProgress<TimeSpan>(value => { if (value > TimeSpan.Zero) fallbackStop.Cancel(); });
        try { await new QualityEvaluationService(executable).EvaluatePartialAsync(reference10, candidate10, QualityMetric.Vmaf, [new(0, 90)], fallbackTrigger, fallbackStop.Token); throw new InvalidOperationException("先頭復号の中止が無効です。"); }
        catch (OperationCanceledException) { }
        var restarted = await service.EvaluatePartialAsync(reference, candidate, ranges, QualityMetric.Ssim, null, CancellationToken.None);
        if (restarted.Frames.Count != 9) throw new InvalidOperationException("実評価の再実行に失敗しました。");
        Directory.CreateDirectory(reportDirectory);
        await File.WriteAllTextAsync(Path.Combine(reportDirectory, "production-api.json"), JsonSerializer.Serialize(new
        { Results = rows, BeforeStart = true, DuringContinuous = true, DuringFallback = true, Restart = true }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    { public void Report(T value) => report(value); }
}
