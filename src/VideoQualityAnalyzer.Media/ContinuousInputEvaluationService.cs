using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using VideoQualityAnalyzer.Core;

namespace VideoQualityAnalyzer.Media;

// 復号器2本から有界のパイプへ送り、一つの評価器の時間特徴を維持する。
public sealed class ContinuousInputEvaluationService(string executable, string probeExecutable, Action<QualityEvaluationTiming>? timing = null)
{
    private sealed record Raster(string PixelFormat, double? Start, string Parameters);

    public async Task<QualityResult> EvaluatePartialAsync(VideoDescriptor reference, VideoDescriptor candidate,
        IReadOnlyList<EvaluationFrameRange> ranges, QualityMetric metric, IProgress<InputSeekProgress>? progress, CancellationToken token)
        => (await ExecuteCoreAsync(reference, candidate, ranges, metric, false, progress, token, compare: false)).Run.Result;

    public async Task<InputSeekComparison> CompareAsync(VideoDescriptor reference, VideoDescriptor candidate,
        IReadOnlyList<EvaluationFrameRange> ranges, QualityMetric metric, bool verifyPixels,
        IProgress<InputSeekProgress>? progress, CancellationToken token)
        => (await ExecuteCoreAsync(reference, candidate, ranges, metric, verifyPixels, progress, token, compare: true)).Comparison!;

    private async Task<(InputSeekRun Run, InputSeekComparison? Comparison)> ExecuteCoreAsync(VideoDescriptor reference, VideoDescriptor candidate,
        IReadOnlyList<EvaluationFrameRange> ranges, QualityMetric metric, bool verifyPixels,
        IProgress<InputSeekProgress>? progress, CancellationToken token, bool compare)
    {
        token.ThrowIfCancellationRequested();
        EvaluationRangeValidator.ValidateCfrPair(reference, candidate, ranges);
        if (!Enum.IsDefined(metric)) throw new ArgumentException("評価指標が不正です。");
        if (reference.Width <= 0 || reference.Height <= 0) throw new ArgumentException("動画の解像度が不正です。");
        if (compare && (reference.Width % 2 != 0 || reference.Height % 2 != 0))
            throw new NotSupportedException("連続入力の試作は、偶数解像度のyuv420p素材が対象です。");
        var snapshot = ranges.ToArray();
        var referenceRaster = await ReadRasterAsync(reference.Path, token);
        var candidateRaster = await ReadRasterAsync(candidate.Path, token);
        var usePipes = OperatingSystem.IsWindows() && reference.Width % 2 == 0 && reference.Height % 2 == 0 &&
            referenceRaster.PixelFormat == "yuv420p" && candidateRaster.PixelFormat == "yuv420p" && referenceRaster.Start is not null && candidateRaster.Start is not null;
        if (compare && !usePipes)
            throw new NotSupportedException("連続入力の試作は、偶数解像度のyuv420p素材が対象です。");
        var numbers = snapshot.SelectMany(range => Enumerable.Range(0, checked((int)(range.End - range.Start))).Select(offset => range.Start + offset)).ToArray();
        var fps = reference.AverageFrameRate.ToString("R", CultureInfo.InvariantCulture);
        if (!compare)
        {
            var run = usePipes ? await RunAsync(true) : await RunDirectAsync();
            run = run with { Result = run.Result with { PartialVersion = QualityResult.CurrentPartialVersion,
                Method = usePipes ? QualityEvaluationMethod.PartialContinuous : QualityEvaluationMethod.PartialSequential } };
            return (run, null);
        }
        var direct = await RunDirectAsync();
        var sequential = verifyPixels ? await RunAsync(false) : direct;
        if (verifyPixels && (direct.Result.Frames.Count != sequential.Result.Frames.Count ||
            direct.Result.Frames.Zip(sequential.Result.Frames).Any(pair => pair.First.Number != pair.Second.Number || Math.Abs(pair.First.Value - pair.Second.Value) > 0.000001)))
            throw new InvalidDataException("パイプ経由の先頭復号が、元の色情報を保った直接評価基準と一致しません。");
        var seek = await RunAsync(true);
        return (seek, InputSeekComparison.Create(sequential, seek));

        async Task<InputSeekRun> RunAsync(bool useSeek)
        {
            var clock = Stopwatch.StartNew();
            long cpuTicks = 0;
            var directory = Path.Combine(Path.GetTempPath(), "VideoQualityAnalyzer", "continuous-" + Guid.NewGuid().ToString("N"));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
            var referenceName = "vqa-ref-" + Guid.NewGuid().ToString("N");
            var candidateName = "vqa-dist-" + Guid.NewGuid().ToString("N");
            await using var referencePipe = new NamedPipeServerStream(referenceName, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 65536, 65536);
            await using var candidatePipe = new NamedPipeServerStream(candidateName, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 65536, 65536);
            Directory.CreateDirectory(directory);
            var referenceHashes = new List<EvaluationFrameDigest>();
            var candidateHashes = new List<EvaluationFrameDigest>();
            var stage = useSeek ? "連続入力シーク" : "連続入力・先頭復号";
            Exception? failure = null;
            try
            {
                var arguments = new List<string> { "-hide_banner", "-loglevel", "error", "-stats_period", "0.1" };
                InputByName(candidateName); InputByName(referenceName);
                void InputByName(string name) => arguments.AddRange(["-thread_queue_size", "2", "-f", "rawvideo", "-pixel_format", "yuv420p",
                    "-video_size", $"{reference.Width}x{reference.Height}", "-framerate", fps, "-probesize", "32", "-analyzeduration", "0", "-i", @"\\.\pipe\" + name]);
                var filter = MetricFilter(metric);
                arguments.AddRange(["-filter_complex", $"[0:v:0]{candidateRaster.Parameters},settb=AVTB,setpts=N/({fps}*TB)[d];" +
                    $"[1:v:0]{referenceRaster.Parameters},settb=AVTB,setpts=N/({fps}*TB)[r];[d][r]{filter}",
                    "-an", "-fps_mode", "passthrough", "-progress", "pipe:1", "-f", "null", "-"]);
                void Report(long count) => progress?.Report(PartialEvaluationProgress.FromCompletedFrames(
                    stage, snapshot, reference.AverageFrameRate, count, numbers.LongLength));
                Report(0);
                async Task Guard(Func<Task> action)
                {
                    try { await action(); }
                    catch (Exception error)
                    {
                        if (error is not OperationCanceledException) Interlocked.CompareExchange(ref failure, error, null);
                        linked.Cancel(); throw;
                    }
                }
                await Task.WhenAll(
                    Task.Run(() => Guard(() => EvaluateAsync(arguments, directory, Report, linked.Token, cpu => Interlocked.Add(ref cpuTicks, cpu.Ticks)))),
                    Task.Run(() => Guard(() => FeedAsync(reference, referenceRaster, referencePipe, referenceHashes, linked.Token))),
                    Task.Run(() => Guard(() => FeedAsync(candidate, candidateRaster, candidatePipe, candidateHashes, linked.Token))));
                token.ThrowIfCancellationRequested();
                var aggregation = Stopwatch.StartNew();
                var result = await ReadResultAsync(directory, numbers, metric, token);
                aggregation.Stop();
                Report(numbers.LongLength); clock.Stop();
                if (!compare) timing?.Invoke(new(clock.Elapsed - aggregation.Elapsed, aggregation.Elapsed, TimeSpan.FromTicks(cpuTicks)));
                return new(result, referenceHashes, candidateHashes, clock.Elapsed);

                async Task FeedAsync(VideoDescriptor video, Raster raster, NamedPipeServerStream pipe, List<EvaluationFrameDigest> hashes, CancellationToken stop)
                {
                    await pipe.WaitForConnectionAsync(stop);
                    var frameBytes = checked(video.Width * video.Height * 3 / 2);
                    var buffer = ArrayPool<byte>.Shared.Rent(frameBytes);
                    try
                    {
                        var batches = useSeek ? snapshot.Select(range => new[] { range }).ToArray() : [snapshot];
                        foreach (var batch in batches)
                        {
                            stop.ThrowIfCancellationRequested();
                            var decoderArguments = new List<string> { "-hide_banner", "-loglevel", "error", "-copyts" };
                            if (useSeek)
                            {
                                var anchor = Math.Max(0, batch[0].Start / reference.AverageFrameRate - 1);
                                decoderArguments.AddRange(["-ss", anchor.ToString("F9", CultureInfo.InvariantCulture), "-t",
                                    (batch[^1].End / reference.AverageFrameRate - anchor + 1).ToString("F9", CultureInfo.InvariantCulture)]);
                            }
                            var select = useSeek
                                ? $"select='between(round((t-{raster.Start!.Value.ToString("R", CultureInfo.InvariantCulture)})*{fps}),{batch[0].Start},{batch[^1].End - 1})'"
                                : $"trim=end_frame={batch[^1].End},select='" + string.Join("+", batch.Select(range => $"between(n,{range.Start},{range.End - 1})")) + "'";
                            decoderArguments.AddRange(["-i", Path.GetFullPath(video.Path), "-map", "0:v:0", "-vf", select, "-an", "-sn", "-dn",
                                "-c:v", "rawvideo", "-threads:v", "1", "-pix_fmt", "yuv420p", "-fps_mode", "passthrough", "-f", "rawvideo", "pipe:1"]);
                            using var decoder = EvaluationProcess.Start(executable, decoderArguments, directory, "連続入力用FFmpegを起動できません。");
                            using var registration = stop.Register(() => EvaluationProcess.Kill(decoder));
                            var errors = decoder.StandardError.ReadToEndAsync();
                            try
                            {
                                foreach (var range in batch)
                                for (var number = range.Start; number < range.End; number++)
                                {
                                    await decoder.StandardOutput.BaseStream.ReadExactlyAsync(buffer.AsMemory(0, frameBytes), stop);
                                    if (verifyPixels) hashes.Add(new(number, frameBytes, Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, frameBytes))).ToLowerInvariant()));
                                    await pipe.WriteAsync(buffer.AsMemory(0, frameBytes), stop);
                                }
                                if (await decoder.StandardOutput.BaseStream.ReadAsync(buffer.AsMemory(0, 1), stop) != 0)
                                    throw new InvalidDataException("復号器が対象数を超えるフレームを返しました。");
                                await decoder.WaitForExitAsync(stop);
                                if (decoder.ExitCode != 0) throw new InvalidOperationException("区間復号に失敗しました。");
                            }
                            finally { EvaluationProcess.Kill(decoder); await decoder.WaitForExitAsync(CancellationToken.None); await errors; Interlocked.Add(ref cpuTicks, decoder.TotalProcessorTime.Ticks); }
                        }
                    }
                    catch (EndOfStreamException) { throw new InvalidDataException("区間復号のフレームが不足しています。"); }
                    finally { ArrayPool<byte>.Shared.Return(buffer); await pipe.DisposeAsync(); }
                }
            }
            catch
            {
                token.ThrowIfCancellationRequested();
                if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
                throw;
            }
            finally { Directory.Delete(directory, recursive: true); }
        }
        async Task<InputSeekRun> RunDirectAsync()
        {
            var clock = Stopwatch.StartNew();
            long cpuTicks = 0;
            var directory = Path.Combine(Path.GetTempPath(), "VideoQualityAnalyzer", "direct-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var selection = $"trim=end_frame={snapshot[^1].End},select='" + string.Join("+", snapshot.Select(range => $"between(n,{range.Start},{range.End - 1})")) +
                    $"',settb=AVTB,setpts=N/({fps}*TB)";
                var filter = MetricFilter(metric);
                var arguments = new[] { "-hide_banner", "-loglevel", "error", "-stats_period", "0.1", "-i", Path.GetFullPath(candidate.Path), "-i", Path.GetFullPath(reference.Path),
                    "-filter_complex", $"[0:v:0]{selection}[d];[1:v:0]{selection}[r];[d][r]{filter}", "-an", "-fps_mode", "passthrough", "-progress", "pipe:1", "-f", "null", "-" };
                void Report(long count) => progress?.Report(PartialEvaluationProgress.FromCompletedFrames(
                    "直接復号基準", snapshot, reference.AverageFrameRate, count, numbers.LongLength));
                Report(0);
                await Task.Run(() => EvaluateAsync(arguments, directory, Report, token, cpu => Interlocked.Add(ref cpuTicks, cpu.Ticks)));
                var aggregation = Stopwatch.StartNew();
                var result = await ReadResultAsync(directory, numbers, metric, token);
                aggregation.Stop();
                Report(numbers.LongLength); clock.Stop();
                if (!compare) timing?.Invoke(new(clock.Elapsed - aggregation.Elapsed, aggregation.Elapsed, TimeSpan.FromTicks(cpuTicks)));
                return new(result, [], [], clock.Elapsed);
            }
            finally { Directory.Delete(directory, recursive: true); }
        }
    }

    private static string MetricFilter(QualityMetric metric) => metric switch
    {
        QualityMetric.Ssim => "ssim=stats_file=metric.log:shortest=1",
        QualityMetric.Psnr => "psnr=stats_file=metric.log:shortest=1",
        _ => "libvmaf=log_fmt=json:log_path=metric.json:n_threads=1:shortest=1"
    };

    private static async Task<QualityResult> ReadResultAsync(string directory, long[] numbers, QualityMetric metric, CancellationToken token)
    {
        var parsed = metric == QualityMetric.Vmaf
            ? QualityEvaluationService.ParseVmaf(await File.ReadAllTextAsync(Path.Combine(directory, "metric.json"), token))
            : QualityEvaluationService.ParseStats(metric, await File.ReadAllTextAsync(Path.Combine(directory, "metric.log"), token));
        if (parsed.Frames.Any(frame => frame.Number < 0 || frame.Number >= numbers.LongLength || !double.IsFinite(frame.Value)) ||
            parsed.Frames.Select(frame => frame.Number).Distinct().Count() != parsed.Frames.Count ||
            metric != QualityMetric.Psnr && !parsed.Frames.Select(frame => frame.Number).SequenceEqual(Enumerable.Range(0, numbers.Length).Select(number => (long)number)))
            throw new InvalidDataException("連続入力の評価フレーム数・番号が一致しません。");
        return parsed with { Frames = parsed.Frames.Select(frame => frame with { Number = numbers[checked((int)frame.Number)] }).ToArray() };
    }

    private async Task EvaluateAsync(IReadOnlyList<string> arguments, string directory, Action<long> report, CancellationToken token, Action<TimeSpan>? cpu = null)
    {
        using var process = EvaluationProcess.Start(executable, arguments, directory, "連続入力用FFmpegを起動できません。");
        using var registration = token.Register(() => EvaluationProcess.Kill(process));
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            while (await process.StandardOutput.ReadLineAsync(token) is { } line)
                if (line.StartsWith("frame=") && long.TryParse(line.AsSpan(6), out var frames)) report(frames);
            await process.WaitForExitAsync(token);
            token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) throw new InvalidOperationException($"連続評価に失敗しました（終了コード{process.ExitCode}）。");
        }
        finally { EvaluationProcess.Kill(process); await process.WaitForExitAsync(CancellationToken.None); await errors; cpu?.Invoke(process.TotalProcessorTime); }
    }
    private async Task<Raster> ReadRasterAsync(string path, CancellationToken token)
    {
        var arguments = new[] { "-v", "error", "-select_streams", "v:0", "-show_entries",
            "stream=pix_fmt,start_time,color_space,color_range,color_primaries,color_transfer", "-of", "json", "-i", path };
        using var process = EvaluationProcess.Start(probeExecutable, arguments, null, "映像情報を取得できません。");
        using var registration = token.Register(() => EvaluationProcess.Kill(process));
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(token); }
        finally { EvaluationProcess.Kill(process); await process.WaitForExitAsync(CancellationToken.None); await errors; }
        token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new InvalidOperationException("映像形式の取得に失敗しました。");
        using var json = JsonDocument.Parse(await output);
        var stream = json.RootElement.GetProperty("streams")[0];
        string Value(string key, string fallback = "unknown") => stream.TryGetProperty(key, out var value) ? value.GetString() ?? fallback : fallback;
        var range = Value("color_range") switch { "tv" => "limited", "pc" => "full", _ => "unknown" };
        var knownStart = double.TryParse(Value("start_time", "N/A"), NumberStyles.Float, CultureInfo.InvariantCulture, out var start) && double.IsFinite(start);
        return new(Value("pix_fmt"), knownStart ? start : null,
            $"setparams=colorspace={Value("color_space")}:range={range}:color_primaries={Value("color_primaries")}:color_trc={Value("color_transfer")}");
    }
}
