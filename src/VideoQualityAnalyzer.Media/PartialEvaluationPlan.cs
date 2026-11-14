using VideoQualityAnalyzer.Core;

namespace VideoQualityAnalyzer.Media;

public enum PartialEvaluationMode { TimeRange, Periodic, Uniform }
public sealed record EvaluationFrameRange(long Start, long End);

// 区間は元動画の0始まりフレーム番号で、終了番号を含まない。
public static class PartialEvaluationPlan
{
    public static IReadOnlyList<EvaluationFrameRange> Create(VideoDescriptor video, PartialEvaluationMode mode,
        double startSeconds, double endSeconds, double periodSeconds, double lengthSeconds, int count)
    {
        var fps = video.AverageFrameRate;
        var duration = video.Duration.TotalSeconds;
        if (!double.IsFinite(fps) || fps <= 0 || !double.IsFinite(duration) || duration <= 0 || video.IsVariableFrameRate)
            throw new ArgumentException("長さとフレームレートが取得できるCFR動画を指定してください。");
        var total = video.TotalFrames ?? (long)Math.Ceiling(duration * fps);
        var ranges = new List<EvaluationFrameRange>();
        void Add(double start, double end)
        {
            var first = Math.Clamp((long)Math.Ceiling(start * fps - 1e-7), 0, total);
            var last = Math.Clamp((long)Math.Ceiling(end * fps - 1e-7), 0, total);
            if (last <= first) return;
            if (ranges.Count > 0 && first <= ranges[^1].End)
                ranges[^1] = ranges[^1] with { End = Math.Max(last, ranges[^1].End) };
            else ranges.Add(new(first, last));
        }
        if (mode == PartialEvaluationMode.TimeRange)
        {
            if (!double.IsFinite(startSeconds) || !double.IsFinite(endSeconds) || startSeconds < 0 ||
                endSeconds <= startSeconds || endSeconds > duration)
                throw new ArgumentException("開始・終了秒を動画の範囲内で指定してください（開始 < 終了）。");
            Add(startSeconds, endSeconds);
        }
        else
        {
            if (!double.IsFinite(lengthSeconds) || lengthSeconds <= 0 || lengthSeconds > duration)
                throw new ArgumentException("区間の長さは0秒より大きく、動画の長さ以下にしてください。");
            if (mode == PartialEvaluationMode.Periodic)
            {
                if (!double.IsFinite(periodSeconds) || periodSeconds < lengthSeconds || duration / periodSeconds > 1000)
                    throw new ArgumentException("周期は区間長以上、区間数は1000以下にしてください。");
                for (var i = 0; i * periodSeconds < duration; i++)
                    Add(i * periodSeconds, Math.Min(i * periodSeconds + lengthSeconds, duration));
            }
            else if (mode == PartialEvaluationMode.Uniform)
            {
                if (count < 1 || count > 1000 || lengthSeconds * count > duration)
                    throw new ArgumentException("区間数は1～1000、区間長×区間数は動画の長さ以下にしてください。");
                for (var i = 0; i < count; i++)
                {
                    var start = count == 1 ? (duration - lengthSeconds) / 2 : i * (duration - lengthSeconds) / (count - 1);
                    Add(start, start + lengthSeconds);
                }
            }
            else throw new ArgumentException("評価方式が不正です。");
        }
        if (ranges.Count == 0) throw new ArgumentException("対象フレームがありません。");
        return ranges;
    }
}
