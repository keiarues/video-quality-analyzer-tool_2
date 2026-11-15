using System.Diagnostics;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Diagnostics;
using VideoQualityAnalyzer.Media;

var root = args.Length > 0 ? Path.GetFullPath(args[0]) :
    throw new ArgumentException("作業フォルダーのパスを指定してください。");
var probePath = FfprobeService.FindExecutable() ??
    throw new FileNotFoundException("FFprobeが見つかりません。");
var service = new FfprobeService(probePath);
if (args.Contains("--compatibility-baseline"))
{
    string Option(string name) => args[Array.IndexOf(args, name) + 1];
    var compatibilityReference = await service.ProbeAsync(Option("--reference"), CancellationToken.None);
    var compatibilityCandidate = await service.ProbeAsync(Option("--candidate"), CancellationToken.None);
    var ranges = System.Text.Json.JsonSerializer.Deserialize<EvaluationFrameRange[]>(await File.ReadAllTextAsync(Option("--plan")))!;
    var evaluator = new QualityEvaluationService(FfmpegRgbReader.FindExecutable()!);
    var results = new List<QualityResult>();
    foreach (var metric in Enum.GetValues<QualityMetric>())
    {
        Console.WriteLine($"通常評価との比較用：{metric}");
        results.Add(await evaluator.EvaluatePartialAsync(compatibilityReference, compatibilityCandidate, metric, ranges, null, CancellationToken.None));
    }
    await File.WriteAllTextAsync(Option("--output"), System.Text.Json.JsonSerializer.Serialize(results));
    return;
}
if (args.Contains("--quality-timing"))
{
    await QualityTimingBenchmark.RunAsync(root, args);
    return;
}
if (args.Contains("--uniform-partial-test"))
{
    var paths = args.Skip(1).Where(File.Exists).ToArray();
    Check(paths.Length == 2, "均等配置の検証には元動画と比較動画が必要です。");
    var uniformReference = await service.ProbeAsync(paths[0], CancellationToken.None);
    var candidate = await service.ProbeAsync(paths[1], CancellationToken.None);
    var configured = new EvaluationSelection(PartialEvaluationMode.Uniform, LengthSeconds: 10, Count: 6);
    var requested = configured.CreatePlan(uniformReference)!;
    var evaluator = new QualityEvaluationService(FfmpegRgbReader.FindExecutable()!);
    Check(requested[^1].End > EvaluationSelection.FrameCount(candidate), "修正前の末尾範囲外の再現条件");
    try
    {
        await evaluator.EvaluatePartialAsync(uniformReference, candidate, QualityMetric.Ssim, requested, null, CancellationToken.None);
        throw new InvalidOperationException("範囲外の検証が成立しません。");
    }
    catch (ArgumentException) { Console.WriteLine("修正前の均等配置：末尾の範囲外エラーを再現"); }
    var selection = configured.WithComparisonTimeline(uniformReference, [candidate]);
    var ranges = selection.CreatePlan(uniformReference)!;
    var expected = ranges.SelectMany(range => Enumerable.Range(0, checked((int)(range.End - range.Start))).Select(offset => range.Start + offset)).ToArray();
    var result = await evaluator.EvaluatePartialAsync(uniformReference, candidate, QualityMetric.Ssim, ranges, null, CancellationToken.None);
    Check(result.Frames.Select(frame => frame.Number).SequenceEqual(expected) && double.IsFinite(result.Average), "共通範囲の実計算と元番号列");
    Console.WriteLine($"均等配置：元動画 {EvaluationSelection.FrameCount(uniformReference)} / 比較動画 {EvaluationSelection.FrameCount(candidate)}フレーム、共通範囲 {selection.FrameLimit}、{ranges.Count}区間、SSIM {result.Frames.Count}フレーム、平均 {result.Average:F6}：合格");
    return;
}
if (args.Contains("--quality-test"))
{
    var executable = FfmpegRgbReader.FindExecutable()!;
    var directory = Path.Combine(root, "measurement-results", "quality-smoke");
    Directory.CreateDirectory(directory);
    var samples = new List<VideoDescriptor>();
    foreach (var color in new[] { "red", "blue" })
    {
        var path = Path.Combine(directory, color + ".mkv");
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-y", "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i",
            $"color=c={color}:s=64x64:r=30:d=1", "-c:v", "ffv1", path }) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        await process.WaitForExitAsync();
        Check(process.ExitCode == 0, "評価素材の生成");
        samples.Add(await service.ProbeAsync(path, CancellationToken.None));
    }
    var evaluator = new QualityEvaluationService(executable);
    var identical = await evaluator.EvaluateAsync(samples[0], samples[0], QualityMetric.Ssim, null, CancellationToken.None);
    Check(identical.Frames.Count == 30 && Math.Abs(identical.Average - 1) < 0.000001, "同一動画SSIM");
    foreach (var metric in new[] { QualityMetric.Ssim, QualityMetric.Psnr, QualityMetric.Vmaf })
    {
        var result = await evaluator.EvaluateAsync(samples[0], samples[1], metric, null, CancellationToken.None);
        Check(result.Frames.Count == 30 && double.IsFinite(result.Average), "異なる動画の全フレーム評価");
        Console.WriteLine($"{metric}: {result.Frames.Count}フレーム / 平均 {result.Average:F4}");
    }
    using var canceled = new CancellationTokenSource();
    canceled.Cancel();
    try
    {
        await evaluator.EvaluateAsync(samples[0], samples[1], QualityMetric.Ssim, null, canceled.Token);
        throw new InvalidOperationException("中断が反映されません。");
    }
    catch (OperationCanceledException) { Console.WriteLine("中断: 合格"); }
    using var midCancel = new CancellationTokenSource();
    try
    {
        await evaluator.EvaluateAsync(samples[0], samples[1], QualityMetric.Ssim,
            new CancelQualityProgress(midCancel), midCancel.Token);
        throw new InvalidOperationException("実行中の中断が反映されません。");
    }
    catch (OperationCanceledException) { Console.WriteLine("実行中の中断: 合格"); }
    return;
}

var files = Directory.GetFiles(Path.Combine(root, "test-data"));
Check(files.Length == 5, "動画は5本必要です。");

var videos = new List<VideoDescriptor>();
foreach (var file in files)
{
    var video = await service.ProbeAsync(file, CancellationToken.None);
    Check(video.Width == 1440 && video.Height == 1080, $"解像度: {video.Name}");
    Check(Math.Abs(video.AverageFrameRate - 30000d / 1001d) < 0.001, $"fps: {video.Name}");
    Check(!video.HasAudio, $"音声なし: {video.Name}");
    videos.Add(video);
    Console.WriteLine($"{video.Codec,-5} {video.AverageFrameRate:F3} fps {video.Name}");
}

Check(videos.Count(video => video.Codec == "ffv1") == 1, "FFV1素材");
Check(videos.Count(video => video.Codec == "hevc") == 2, "H.265素材");
Check(videos.Count(video => video.Codec == "av1") == 2, "AV1素材");

var ffmpegPath = FfmpegRgbReader.FindExecutable() ??
    throw new FileNotFoundException("FFmpegが見つかりません。");
var comparison = new FrameComparisonService(ffmpegPath, probePath);
var referenceFrame = await comparison.ReadFrameAsync(
    videos.Single(video => video.Codec == "ffv1").Path, 150, CancellationToken.None);
Check(referenceFrame.Pixels.Length == 1440 * 1080 * 4, "参照フレームを復号");
Check(FrameComparisonService.CreateHeatmap(referenceFrame, referenceFrame)
    .MeanAbsoluteRgbDifference == 0, "同一フレームの差は0");
foreach (var video in videos.Where(video => video.Codec != "ffv1"))
{
    var encodedFrame = await comparison.ReadFrameAsync(video.Path, 150, CancellationToken.None);
    Check(encodedFrame.Pixels.Length == referenceFrame.Pixels.Length, $"比較フレームを復号: {video.Name}");
    var difference = FrameComparisonService.CreateHeatmap(referenceFrame, encodedFrame);
    Check(difference.MeanAbsoluteRgbDifference > 0, $"圧縮差を検出: {video.Name}");
    Check(difference.MeanAbsoluteRgbDifference ==
        FrameComparisonService.CreateHeatmap(referenceFrame, encodedFrame, 4).MeanAbsoluteRgbDifference,
        $"差分強調は元の平均差を変えない: {video.Name}");
    Console.WriteLine($"参照 vs {video.Name}: フレーム150の平均RGB差 {difference.MeanAbsoluteRgbDifference:F2}");
}

var reference = videos.Single(video => video.Codec == "ffv1");
var firstComparison = videos.First(video => video.Id != reference.Id);
var empty = SelectionService.Resolve(videos, reference.Id, [], null, 1);
Check(empty.LeftVideoId == reference.Id && empty.RightVideoId == firstComparison.Id, "選択なし");

var single = SelectionService.Resolve(videos, reference.Id, [firstComparison.Id], null, 2);
Check(single.LeftVideoId == reference.Id && single.RightVideoId == firstComparison.Id, "比較動画1本選択");

var pair = videos.Where(video => video.Id != reference.Id).Take(2).ToArray();
var multiple = SelectionService.Resolve(videos, reference.Id, pair.Select(video => video.Id).ToArray(), null, 3);
Check(multiple.LeftVideoId == pair[0].Id && multiple.RightVideoId == pair[1].Id, "比較動画2本選択");

var previous = SelectionService.Resolve(videos, reference.Id, [], pair[1].Id, 4);
Check(previous.RightVideoId == pair[1].Id, "直前の右動画を維持");

var log = new MeasurementLog(Path.Combine(root, "tests", ".smoke-output"));
log.Record("probe", "left", reference.Id, "ok");
log.RecordPerformance("both", reference.Id, reference.AverageFrameRate,
    $"{reference.Width}x{reference.Height}", reference.Codec, 1.5, 100);
await log.DisposeAsync();
var lines = await File.ReadAllLinesAsync(log.FilePath);
Check(lines.Length == 2 && lines[0].Contains("\"run_id\"") && lines[1].Contains("\"cpu_percent\""), "診断ログ");
Check(!lines.Any(line => line.Contains(reference.Path, StringComparison.OrdinalIgnoreCase)), "ログにフルパスを残さない");

Console.WriteLine("素材確認・選択規則・診断ログ: 合格");

if (args.Contains("--blur-benchmark"))
{
    var target = videos.Single(video => video.Name == "攻殻機動隊_icq50.mp4");
    var targetFrame = await comparison.ReadFrameAsync(target.Path, 150, CancellationToken.None);
    foreach (var radius in new[] { 0, 1, 2, 4 })
    {
        FrameComparisonService.CreateHeatmap(referenceFrame, targetFrame, 16, radius);
        var timer = Stopwatch.StartNew();
        for (var index = 0; index < 10; index++)
        {
            var result = FrameComparisonService.CreateHeatmap(referenceFrame, targetFrame, 16, radius);
            Check(result.MeanAbsoluteRgbDifference > 0, $"ぼかし半径{radius}の差分");
        }
        Console.WriteLine($"ぼかし半径{radius}: 1440×1080画像の平均処理時間 " +
            $"{timer.Elapsed.TotalMilliseconds / 10:F1} ms/フレーム");
    }
}

if (args.Contains("--stream-test"))
{
    var target = videos.Single(video => video.Name == "攻殻機動隊_icq50.mp4");
    var targetFrame = await comparison.ReadFrameAsync(target.Path, 150, CancellationToken.None);
    await using var stream = await StreamingFramePairReader.OpenAsync(ffmpegPath, probePath,
        reference.Path, target.Path, 150, CancellationToken.None);
    var timer = Stopwatch.StartNew();
    for (var index = 0; index < 30; index++)
    {
        var streamed = await stream.ReadNextAsync(CancellationToken.None) ??
            throw new InvalidOperationException("連続比較の30フレームを取得できませんでした。");
        Check(streamed.Left.FrameNumber == 150 + index &&
            streamed.Right.FrameNumber == 150 + index, "連続比較の左右フレーム番号");
        if (index == 0)
        {
            Check(streamed.Left.Pixels.AsSpan().SequenceEqual(referenceFrame.Pixels),
                "連続復号の左先頭と静止比較が一致");
            Check(streamed.Right.Pixels.AsSpan().SequenceEqual(targetFrame.Pixels),
                "連続復号の右先頭と静止比較が一致");
        }
        Check(FrameComparisonService.CreateHeatmap(streamed.Left, streamed.Right)
            .MeanAbsoluteRgbDifference > 0, "再生中の比較差分");
    }
    Console.WriteLine($"連続比較: 30組のフレーム対応・差分を確認、処理 {timer.Elapsed.TotalSeconds:F2}秒");
    var av1 = videos.Where(video => video.Codec == "av1").ToArray();
    var hevc = videos.Where(video => video.Codec == "hevc").ToArray();
    foreach (var (leftVideo, rightVideo) in new[]
        {
            (reference, av1[0]), (av1[0], hevc[0]), (av1[0], av1[1]), (hevc[0], hevc[1])
        })
    {
        await using var other = await StreamingFramePairReader.OpenAsync(ffmpegPath, probePath,
            leftVideo.Path, rightVideo.Path, 150, CancellationToken.None);
        for (var index = 0; index < 5; index++)
        {
            var streamed = await other.ReadNextAsync(CancellationToken.None) ??
                throw new InvalidOperationException("連続比較フレームが不足しています。");
            Check(streamed.Left.FrameNumber == 150 + index &&
                streamed.Right.FrameNumber == 150 + index, "別コーデックの連続フレーム対応");
            Check(FrameComparisonService.CreateHeatmap(streamed.Left, streamed.Right)
                .MeanAbsoluteRgbDifference > 0, "別コーデックの連続差分");
        }
        Console.WriteLine($"連続比較: {leftVideo.Codec}＋{rightVideo.Codec} の5組を確認");
    }
}

if (args.Contains("--benchmark"))
{
    var target = videos.Single(video => video.Name == "攻殻機動隊_icq50.mp4");
    foreach (var frameNumber in new long[] { 150, 450 })
    {
        for (var run = 1; run <= 3; run++)
        {
            var timer = Stopwatch.StartNew();
            var frames = await Task.WhenAll(
                comparison.ReadFrameAsync(reference.Path, frameNumber, CancellationToken.None),
                comparison.ReadFrameAsync(target.Path, frameNumber, CancellationToken.None));
            var decodeMs = timer.Elapsed.TotalMilliseconds;
            var heatmap = FrameComparisonService.CreateHeatmap(frames[0], frames[1]);
            var heatmapMs = timer.Elapsed.TotalMilliseconds - decodeMs;
            Check(heatmap.MeanAbsoluteRgbDifference > 0, "計測したフレームの差分");
            Console.WriteLine($"比較計測 frame={frameNumber}, run={run}, 復号={decodeMs:F1}ms, " +
                $"ヒートマップ={heatmapMs:F1}ms, 合計={timer.Elapsed.TotalMilliseconds:F1}ms");
        }
    }
}

static void Check(bool condition, string label)
{
    if (!condition)
        throw new InvalidOperationException($"確認失敗: {label}");
}

sealed class CancelQualityProgress(CancellationTokenSource source) : IProgress<TimeSpan>
{
    public void Report(TimeSpan value) => source.Cancel();
}
