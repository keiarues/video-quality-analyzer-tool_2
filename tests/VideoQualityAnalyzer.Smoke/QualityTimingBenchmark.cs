using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

// 本体の評価経路と、処理を取り除いた診断用FFmpegコマンドを別々に測る。
internal static class QualityTimingBenchmark
{
    private sealed record Measurement(string Case, int Repeat, double WallSeconds, double FfmpegSeconds,
        double AggregationSeconds, double CpuSeconds, long Frames, double? Average = null,
        int? Mismatches = null, double? MaximumDifference = null);

    public static async Task RunAsync(string root, string[] commandLine)
    {
        string Option(string name, string fallback) => Array.IndexOf(commandLine, name) is var index && index >= 0
            && index + 1 < commandLine.Length ? commandLine[index + 1] : fallback;
        var paths = commandLine.Skip(1).Where(File.Exists).ToArray();
        if (paths.Length != 2) throw new ArgumentException("元動画と比較動画をそれぞれ指定してください。");
        var phase = Option("--phase", "core");
        var repeats = int.Parse(Option("--repeats", "3"), CultureInfo.InvariantCulture);
        var metric = Enum.Parse<QualityMetric>(Option("--metric", "Ssim"), true);
        var ffmpeg = FfmpegRgbReader.FindExecutable()!;
        var probe = new FfprobeService(FfprobeService.FindExecutable()!);
        var reference = await probe.ProbeAsync(paths[0], CancellationToken.None);
        var candidate = await probe.ProbeAsync(paths[1], CancellationToken.None);
        var directory = Path.Combine(root, "measurement-results", "quality-timing",
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + $"-{candidate.Codec}-{phase}-{metric}");
        Directory.CreateDirectory(directory);
        var version = await FfmpegSettings.GetVersionAsync(ffmpeg);
        var rows = new List<Measurement>();
        object Describe(VideoDescriptor video) => new { video.Codec, video.Width, video.Height, video.AverageFrameRate,
            DurationSeconds = video.Duration.TotalSeconds, Frames = EvaluationSelection.FrameCount(video), Bytes = new FileInfo(video.Path).Length };
        var metadata = new { StartedUtc = DateTimeOffset.UtcNow, Phase = phase, Repeats = repeats, Metric = metric.ToString(),
            Os = Environment.OSVersion.VersionString, Runtime = Environment.Version.ToString(), LogicalProcessors = Environment.ProcessorCount,
            Cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"), Ffmpeg = version,
            Reference = Describe(reference), Candidate = Describe(candidate),
            Conditions = "画面なし・1ジョブずつ実行・OSキャッシュ消去なし・開始前に圧縮データの読み込みを1回実施。CPU秒は全スレッドの合計。" };
        async Task SaveAsync()
        {
            var text = JsonSerializer.Serialize(new { Metadata = metadata, Results = rows }, new JsonSerializerOptions { WriteIndented = true });
            var target = Path.Combine(directory, "results.json");
            await File.WriteAllTextAsync(target + ".tmp", text);
            File.Move(target + ".tmp", target, true);
        }
        async Task RecordAsync(Measurement row)
        {
            rows.Add(row); await SaveAsync();
            Console.WriteLine($"{row.Case} #{row.Repeat}: 経過 {row.WallSeconds:F3}秒 / FFmpeg {row.FfmpegSeconds:F3}秒 / 集計 {row.AggregationSeconds:F3}秒 / CPU {row.CpuSeconds:F3}秒 / {row.Frames}フレーム / 不一致 {row.Mismatches?.ToString() ?? "--"}");
        }
        var uniform = new EvaluationSelection(PartialEvaluationMode.Uniform, LengthSeconds: 10, Count: 6)
            .WithComparisonTimeline(reference, [candidate]);
        var uniformRanges = uniform.CreatePlan(reference)!;
        List<string> Inputs() => ["-i", Path.GetFullPath(candidate.Path), "-i", Path.GetFullPath(reference.Path)];
        string Selection(IReadOnlyList<EvaluationFrameRange>? ranges) => ranges is null ? "setpts=PTS-STARTPTS" :
            $"trim=end_frame={ranges[^1].End},select='" + string.Join("+", ranges.Select(range => $"between(n,{range.Start},{range.End - 1})")) +
            $"',setpts=N/({reference.AverageFrameRate.ToString("R", CultureInfo.InvariantCulture)}*TB)";
        List<string> NoMetric(IReadOnlyList<EvaluationFrameRange>? ranges)
        {
            var arguments = Inputs(); var filter = Selection(ranges);
            arguments.AddRange(["-filter_complex", $"[0:v:0]{filter}[dist];[1:v:0]{filter}[ref]", "-map", "[dist]", "-map", "[ref]", "-an", "-f", "null", "-"]);
            return arguments;
        }
        async Task<(double Wall, double Cpu, long Frames)> RawAsync(IReadOnlyList<string> arguments, string workingDirectory)
        {
            var info = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = workingDirectory };
            foreach (var argument in new[] { "-hide_banner", "-loglevel", "error", "-progress", "pipe:1" }.Concat(arguments))
                info.ArgumentList.Add(argument);
            var clock = Stopwatch.StartNew();
            using var process = Process.Start(info)!;
            var errors = process.StandardError.ReadToEndAsync();
            long frames = 0;
            while (await process.StandardOutput.ReadLineAsync() is { } line)
                if (line.StartsWith("frame=") && long.TryParse(line.AsSpan(6), out var parsed)) frames = parsed;
            await process.WaitForExitAsync(); await errors;
            clock.Stop();
            if (process.ExitCode != 0) throw new InvalidOperationException($"診断用FFmpegが失敗しました（終了コード{process.ExitCode}）。");
            return (clock.Elapsed.TotalSeconds, process.TotalProcessorTime.TotalSeconds, frames);
        }
        async Task<QualityResult> ActualAsync(string name, int repeat, EvaluationSelection selection, QualityMetric? overrideMetric = null)
        {
            Console.WriteLine($"開始：{name} #{repeat}");
            QualityEvaluationTiming? timing = null;
            var evaluator = new QualityEvaluationService(ffmpeg, value => timing = value);
            var clock = Stopwatch.StartNew();
            var ranges = selection.CreatePlan(reference);
            var actualMetric = overrideMetric ?? metric;
            var result = ranges is null ? await evaluator.EvaluateAsync(reference, candidate, actualMetric, null, CancellationToken.None)
                : await evaluator.EvaluatePartialAsync(reference, candidate, actualMetric, ranges, null, CancellationToken.None);
            clock.Stop();
            if (timing is null) throw new InvalidOperationException("計測値がありません。");
            await RecordAsync(new(name, repeat, clock.Elapsed.TotalSeconds, timing.FfmpegElapsed.TotalSeconds,
                timing.AggregationElapsed.TotalSeconds, timing.FfmpegCpuTime.TotalSeconds, result.Frames.Count, result.Average));
            return result;
        }

        await SaveAsync();
        Console.WriteLine($"測定開始：{candidate.Codec} / {phase} / {metric} / {repeats}回。結果：{Path.GetRelativePath(root, directory)}");
        var copy = Inputs(); copy.AddRange(["-map", "0:v:0", "-map", "1:v:0", "-c:v", "copy", "-an", "-f", "null", "-"]);
        var warmup = await RawAsync(copy, directory);
        await RecordAsync(new("warmup_demux", 0, warmup.Wall, warmup.Wall, 0, warmup.Cpu, warmup.Frames));

        if (phase == "core" || phase == "metrics")
        {
            var cases = new List<(string Name, EvaluationSelection Selection)> { ("full", EvaluationSelection.Full) };
            if (phase == "core")
            {
                cases.Add(("range_240_250", new EvaluationSelection(PartialEvaluationMode.TimeRange, 240, 250).WithComparisonTimeline(reference, [candidate])));
                cases.Add(("periodic_60_10", new EvaluationSelection(PartialEvaluationMode.Periodic, PeriodSeconds: 60, LengthSeconds: 10).WithComparisonTimeline(reference, [candidate])));
            }
            cases.Add(("uniform_10x6", uniform));
            for (var repeat = 1; repeat <= repeats; repeat++)
                for (var index = 0; index < cases.Count; index++)
                {
                    var item = cases[(index + repeat - 1) % cases.Count];
                    await ActualAsync(item.Name, repeat, item.Selection);
                }
        }
        else if (phase == "ablation")
        {
            var candidates = new List<(string Name, List<string> Arguments)>
            {
                ("demux_copy_both", copy),
                ("decode_reference", ["-i", reference.Path, "-map", "0:v:0", "-an", "-f", "null", "-"]),
                ("decode_candidate", ["-i", candidate.Path, "-map", "0:v:0", "-an", "-f", "null", "-"]),
                ("decode_both", NoMetric(null)),
                ("select_uniform_no_metric", NoMetric(uniformRanges)),
                ("select_first10_no_metric", NoMetric(new EvaluationSelection(PartialEvaluationMode.TimeRange, 0, 10).CreatePlan(reference)))
            };
            for (var repeat = 1; repeat <= repeats; repeat++)
                for (var index = 0; index < candidates.Count; index++)
                {
                    var item = candidates[(index + repeat - 1) % candidates.Count];
                    Console.WriteLine($"開始：{item.Name} #{repeat}");
                    var measured = await RawAsync(item.Arguments, directory);
                    await RecordAsync(new(item.Name, repeat, measured.Wall, measured.Wall, 0, measured.Cpu, measured.Frames));
                }
        }
        else if (phase == "fusion")
        {
            var ssim = await ActualAsync("separate_ssim_uniform", 1, uniform, QualityMetric.Ssim);
            var psnr = await ActualAsync("separate_psnr_uniform", 1, uniform, QualityMetric.Psnr);
            var numbers = uniformRanges.SelectMany(range => Enumerable.Range(0, checked((int)(range.End - range.Start))).Select(offset => range.Start + offset)).ToArray();
            for (var repeat = 1; repeat <= repeats; repeat++)
            {
                Console.WriteLine($"開始：fused_ssim_psnr_uniform #{repeat}");
                var filter = Selection(uniformRanges);
                var arguments = Inputs();
                arguments.AddRange(["-filter_complex", $"[0:v:0]{filter},split=2[ds][dp];[1:v:0]{filter},split=2[rs][rp];" +
                    "[ds][rs]ssim=stats_file=ssim.log:shortest=1[s];[dp][rp]psnr=stats_file=psnr.log:shortest=1[p]",
                    "-map", "[s]", "-map", "[p]", "-an", "-f", "null", "-"]);
                var clock = Stopwatch.StartNew();
                var measured = await RawAsync(arguments, directory);
                var aggregation = Stopwatch.StartNew();
                var ssimResult = QualityEvaluationService.ParseStats(QualityMetric.Ssim, await File.ReadAllTextAsync(Path.Combine(directory, "ssim.log")));
                var psnrResult = QualityEvaluationService.ParseStats(QualityMetric.Psnr, await File.ReadAllTextAsync(Path.Combine(directory, "psnr.log")));
                int mismatches = 0; double maximum = 0;
                foreach (var (expected, actual) in new[] { (ssim, ssimResult), (psnr, psnrResult) })
                {
                    var mapped = actual.Frames.ToDictionary(frame => numbers[checked((int)frame.Number)], frame => frame.Value);
                    mismatches += Math.Abs(expected.Frames.Count - mapped.Count);
                    foreach (var frame in expected.Frames)
                    {
                        if (!mapped.TryGetValue(frame.Number, out var value)) { mismatches++; continue; }
                        var difference = Math.Abs(frame.Value - value); maximum = Math.Max(maximum, difference);
                        if (difference > 0.000001) mismatches++;
                    }
                }
                aggregation.Stop(); clock.Stop();
                await RecordAsync(new("fused_ssim_psnr_uniform", repeat, clock.Elapsed.TotalSeconds, measured.Wall,
                    aggregation.Elapsed.TotalSeconds, measured.Cpu, ssimResult.Frames.Count, ssimResult.Average, mismatches, maximum));
            }
        }
        else if (phase == "vmaf-threads")
        {
            var baseline = await ActualAsync("vmaf_default_uniform", 1, uniform, QualityMetric.Vmaf);
            var numbers = uniformRanges.SelectMany(range => Enumerable.Range(0, checked((int)(range.End - range.Start))).Select(offset => range.Start + offset)).ToArray();
            for (var repeat = 1; repeat <= repeats; repeat++)
            {
                Console.WriteLine($"開始：vmaf_threads8_uniform #{repeat}");
                var filter = Selection(uniformRanges); var arguments = Inputs();
                arguments.AddRange(["-filter_complex", $"[0:v:0]{filter}[dist];[1:v:0]{filter}[ref];[dist][ref]" +
                    "libvmaf=log_fmt=json:log_path=vmaf-threads.json:shortest=1:n_threads=8", "-an", "-f", "null", "-"]);
                var clock = Stopwatch.StartNew();
                var measured = await RawAsync(arguments, directory);
                var aggregation = Stopwatch.StartNew();
                var result = QualityEvaluationService.ParseVmaf(await File.ReadAllTextAsync(Path.Combine(directory, "vmaf-threads.json")));
                var values = result.Frames.Select(frame => frame with { Number = numbers[checked((int)frame.Number)] }).ToArray();
                var differences = baseline.Frames.Zip(values).Select(pair => Math.Abs(pair.First.Value - pair.Second.Value)).ToArray();
                var mismatches = Math.Abs(baseline.Frames.Count - values.Length) + differences.Count(value => value > 0.000001);
                aggregation.Stop(); clock.Stop();
                await RecordAsync(new("vmaf_threads8_uniform", repeat, clock.Elapsed.TotalSeconds, measured.Wall, aggregation.Elapsed.TotalSeconds,
                    measured.Cpu, values.Length, result.Average, mismatches, differences.DefaultIfEmpty(0).Max()));
            }
        }
        else if (phase is "seek" or "seek-exact")
        {
            if (metric != QualityMetric.Ssim) throw new ArgumentException("シーク試作の照合はSSIMを使用してください。");
            var baseline = await ActualAsync("uniform_baseline", 1, uniform);
            var exact = phase == "seek-exact";
            var referenceStart = exact ? (await probe.ReadVideoStartTimeAsync(reference.Path, CancellationToken.None)).TotalSeconds : 0;
            var candidateStart = exact ? (await probe.ReadVideoStartTimeAsync(candidate.Path, CancellationToken.None)).TotalSeconds : 0;
            for (var repeat = 1; repeat <= repeats; repeat++)
            {
                var name = exact ? "seek_exact_uniform" : "seek_uniform";
                Console.WriteLine($"開始：{name} #{repeat}");
                var clock = Stopwatch.StartNew();
                double cpu = 0; var values = new List<QualityFrame>();
                foreach (var range in uniformRanges)
                {
                    var startSeconds = Math.Max(0, range.Start / reference.AverageFrameRate - (exact ? 1 : 0));
                    var start = startSeconds.ToString("F9", CultureInfo.InvariantCulture);
                    var duration = (range.End / reference.AverageFrameRate - startSeconds + 1).ToString("F9", CultureInfo.InvariantCulture);
                    string Interval(double streamStart) => exact ?
                        $"select='between(round((t-{streamStart.ToString("R", CultureInfo.InvariantCulture)})*{reference.AverageFrameRate.ToString("R", CultureInfo.InvariantCulture)}),{range.Start},{range.End - 1})',setpts=N/({reference.AverageFrameRate.ToString("R", CultureInfo.InvariantCulture)}*TB)" : "setpts=PTS-STARTPTS";
                    var arguments = new List<string>();
                    if (exact) arguments.Add("-copyts");
                    arguments.AddRange(["-ss", start]);
                    if (exact) arguments.AddRange(["-t", duration]);
                    arguments.AddRange(["-i", candidate.Path, "-ss", start]);
                    if (exact) arguments.AddRange(["-t", duration]);
                    arguments.AddRange(["-i", reference.Path,
                        "-filter_complex", $"[0:v:0]{Interval(candidateStart)}[dist];[1:v:0]{Interval(referenceStart)}[ref];[dist][ref]ssim=stats_file=metric.log:shortest=1",
                        "-frames:v", (range.End - range.Start).ToString(CultureInfo.InvariantCulture), "-an", "-f", "null", "-"]);
                    var measured = await RawAsync(arguments, directory); cpu += measured.Cpu;
                    var result = QualityEvaluationService.ParseStats(metric, await File.ReadAllTextAsync(Path.Combine(directory, "metric.log")));
                    values.AddRange(result.Frames.Select(frame => frame with { Number = frame.Number + range.Start }));
                }
                clock.Stop();
                var matched = baseline.Frames.Zip(values).ToArray();
                var mismatches = Math.Abs(baseline.Frames.Count - values.Count) + matched.Count(pair => pair.First.Number != pair.Second.Number || Math.Abs(pair.First.Value - pair.Second.Value) > 0.000001);
                var maximum = matched.Select(pair => Math.Abs(pair.First.Value - pair.Second.Value)).DefaultIfEmpty(0).Max();
                await RecordAsync(new(name, repeat, clock.Elapsed.TotalSeconds, clock.Elapsed.TotalSeconds, 0, cpu,
                    values.Count, values.Average(frame => frame.Value), mismatches, maximum));
            }
        }
        else throw new ArgumentException("測定段階を確認してください。");
        Console.WriteLine("測定完了");
    }
}
