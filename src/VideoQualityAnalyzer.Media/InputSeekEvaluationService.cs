using System.Diagnostics;
using System.Globalization;
using VideoQualityAnalyzer.Core;

namespace VideoQualityAnalyzer.Media;

// 専用画面用。通常評価とは別に、画素ハッシュを同時出力してシークの正確さを検証する。
public sealed class InputSeekEvaluationService(string executable, string probeExecutable)
{
    public async Task<InputSeekComparison> CompareAsync(VideoDescriptor reference, VideoDescriptor candidate,
        IReadOnlyList<EvaluationFrameRange> ranges, QualityMetric metric, IProgress<InputSeekProgress>? progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Validate(reference, candidate, ranges, metric);
        var snapshot = ranges.ToArray();
        var probe = new FfprobeService(probeExecutable);
        var referenceStart = await probe.ReadVideoStartTimeAsync(reference.Path, token);
        var candidateStart = await probe.ReadVideoStartTimeAsync(candidate.Path, token);
        var directory = Path.Combine(Path.GetTempPath(), "VideoQualityAnalyzer", "seek-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var sequential = await RunAsync(false);
            var seek = await RunAsync(true);
            return InputSeekComparison.Create(sequential, seek);

            async Task<InputSeekRun> RunAsync(bool useSeek)
            {
                var clock = Stopwatch.StartNew();
                var referenceFrames = new List<EvaluationFrameDigest>();
                var candidateFrames = new List<EvaluationFrameDigest>();
                var values = new List<QualityFrame>();
                var total = snapshot.Sum(range => range.End - range.Start);
                var batches = useSeek ? snapshot.Select(range => new[] { range }).ToArray() : [snapshot];
                long completed = 0;
                for (var index = 0; index < batches.Length; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var batch = batches[index];
                    var numbers = batch.SelectMany(range => Enumerable.Range(0, checked((int)(range.End - range.Start)))
                        .Select(offset => range.Start + offset)).ToArray();
                    void Report(long frames)
                    {
                        var processed = Math.Clamp(completed + Math.Clamp(frames, 0, numbers.Length), 0, total);
                        progress?.Report(new(useSeek ? "区間シーク" : "先頭から復号", index + 1, batches.Length, processed, total,
                            PartialEvaluationProgress.ToSourceTime(snapshot, reference.AverageFrameRate,
                                TimeSpan.FromSeconds(processed / reference.AverageFrameRate))));
                    }
                    Report(0);
                    var arguments = BuildArguments(reference, candidate, batch, metric, useSeek, referenceStart, candidateStart);
                    await ExecuteAsync(arguments, directory, Report, token);
                    token.ThrowIfCancellationRequested();
                    var referenceHashes = ParseFrameHashes(await File.ReadAllTextAsync(Path.Combine(directory, "reference.sha256"), token), numbers);
                    var candidateHashes = ParseFrameHashes(await File.ReadAllTextAsync(Path.Combine(directory, "candidate.sha256"), token), numbers);
                    referenceFrames.AddRange(referenceHashes); candidateFrames.AddRange(candidateHashes);
                    var result = QualityEvaluationService.ParseStats(metric, await File.ReadAllTextAsync(Path.Combine(directory, "metric.log"), token));
                    foreach (var frame in result.Frames)
                    {
                        if (frame.Number < 0 || frame.Number >= numbers.Length)
                            throw new InvalidDataException("評価ログのフレーム番号が対象区間を超えています。");
                        values.Add(frame with { Number = numbers[checked((int)frame.Number)] });
                    }
                    completed += numbers.Length;
                    Report(0);
                }
                clock.Stop();
                return new(new(metric, values.Average(frame => frame.Value), values), referenceFrames, candidateFrames, clock.Elapsed);
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    public static void Validate(VideoDescriptor reference, VideoDescriptor candidate, IReadOnlyList<EvaluationFrameRange> ranges, QualityMetric metric)
    {
        if (metric is not (QualityMetric.Ssim or QualityMetric.Psnr))
            throw new NotSupportedException("この検証はSSIM・PSNRが対象です。VMAFの区間境界の扱いは別途検証します。");
        if (reference.IsVariableFrameRate || candidate.IsVariableFrameRate || reference.AverageFrameRate <= 0 ||
            !double.IsFinite(reference.AverageFrameRate) || !double.IsFinite(candidate.AverageFrameRate) || candidate.AverageFrameRate <= 0 ||
            Math.Abs(reference.AverageFrameRate - candidate.AverageFrameRate) > 0.001 ||
            reference.Width != candidate.Width || reference.Height != candidate.Height)
            throw new ArgumentException("同解像度・同fpsのCFR動画を指定してください。");
        if (ranges.Count == 0 || ranges.Any(range => range.Start < 0 || range.End <= range.Start || range.End - range.Start > int.MaxValue) ||
            ranges.Zip(ranges.Skip(1)).Any(pair => pair.First.End > pair.Second.Start) ||
            ranges[^1].End > Math.Min(EvaluationSelection.FrameCount(reference), EvaluationSelection.FrameCount(candidate)))
            throw new ArgumentException("動画の共通範囲内で、昇順かつ重複しない評価区間を指定してください。");
    }

    private static IReadOnlyList<string> BuildArguments(VideoDescriptor reference, VideoDescriptor candidate,
        IReadOnlyList<EvaluationFrameRange> ranges, QualityMetric metric, bool useSeek, TimeSpan referenceStart, TimeSpan candidateStart)
    {
        var fps = reference.AverageFrameRate.ToString("R", CultureInfo.InvariantCulture);
        var arguments = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-copyts" };
        void Input(VideoDescriptor video, TimeSpan streamStart)
        {
            if (useSeek)
            {
                var anchor = Math.Max(0, ranges[0].Start / reference.AverageFrameRate - 1);
                var duration = ranges[^1].End / reference.AverageFrameRate - anchor + 1;
                arguments.AddRange(["-ss", anchor.ToString("F9", CultureInfo.InvariantCulture),
                    "-t", duration.ToString("F9", CultureInfo.InvariantCulture)]);
            }
            arguments.AddRange(["-i", Path.GetFullPath(video.Path)]);
        }
        Input(candidate, candidateStart); Input(reference, referenceStart);
        string Select(TimeSpan streamStart) => useSeek
            ? $"select='between(round((t-{streamStart.TotalSeconds.ToString("R", CultureInfo.InvariantCulture)})*{fps}),{ranges[0].Start},{ranges[^1].End - 1})'"
            : $"trim=end_frame={ranges[^1].End},select='" + string.Join("+", ranges.Select(range => $"between(n,{range.Start},{range.End - 1})")) + "'";
        // 入力のtime_baseが異なっても、同じ番号の左右フレームを正確に対応させる。
        // メタデータ差による自動色変換がハッシュ分岐へ回り込むのを防ぎ、復号したYUVサンプルを照合する。
        string Prepare(TimeSpan streamStart) => "setparams=colorspace=unknown," + Select(streamStart) + $",settb=AVTB,setpts=N/({fps}*TB),split=2";
        var metricFilter = metric == QualityMetric.Ssim ? "ssim" : "psnr";
        arguments.AddRange(["-filter_complex", $"[0:v:0]{Prepare(candidateStart)}[dist][dh];[1:v:0]{Prepare(referenceStart)}[ref][rh];" +
            $"[dist][ref]{metricFilter}=stats_file=metric.log:shortest=1[q]", "-map", "[q]", "-an", "-fps_mode", "passthrough", "-f", "null", "-"]);
        foreach (var (stream, file) in new[] { ("[rh]", "reference.sha256"), ("[dh]", "candidate.sha256") })
            arguments.AddRange(["-map", stream, "-an", "-c:v", "rawvideo", "-threads:v", "1", "-fps_mode", "passthrough", "-f", "framehash", "-hash", "sha256", file]);
        arguments.AddRange(["-progress", "pipe:1"]);
        return arguments;
    }

    private async Task ExecuteAsync(IReadOnlyList<string> arguments, string directory, Action<long> report, CancellationToken token)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = directory };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("検証用FFmpegを起動できません。");
        using var registration = token.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            while (await process.StandardOutput.ReadLineAsync(token) is { } line)
                if (line.StartsWith("frame=") && long.TryParse(line.AsSpan(6), out var frames)) report(frames);
            await process.WaitForExitAsync(token);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None); await errors;
        }
        token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
        {
            var message = await errors;
            for (var index = 0; index + 1 < arguments.Count; index++)
                if (arguments[index] == "-i") message = message.Replace(arguments[index + 1], "[入力動画]");
            throw new InvalidOperationException($"検証用FFmpegの処理に失敗しました（終了コード{process.ExitCode}）。\n{message}");
        }
    }

    public static IReadOnlyList<EvaluationFrameDigest> ParseFrameHashes(string text, IReadOnlyList<long> sourceNumbers)
    {
        var rows = text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#')).ToArray();
        if (rows.Length != sourceNumbers.Count)
            throw new InvalidDataException($"画素照合用のフレーム数が一致しません（期待{sourceNumbers.Count}、取得{rows.Length}）。");
        return rows.Select((line, index) =>
        {
            var fields = line.Split(',', StringSplitOptions.TrimEntries);
            if (fields.Length != 6 || !long.TryParse(fields[4], out var bytes) || bytes <= 0 || fields[5].Length != 64 ||
                fields[5].Any(character => !Uri.IsHexDigit(character))) throw new InvalidDataException("画素ハッシュの形式が不正です。");
            return new EvaluationFrameDigest(sourceNumbers[index], bytes, fields[5].ToLowerInvariant());
        }).ToArray();
    }
}
