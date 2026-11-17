using VideoQualityAnalyzer.Core;

namespace VideoQualityAnalyzer.Media;

internal static class EvaluationRangeValidator
{
    public static void ValidateCfrPair(VideoDescriptor reference, VideoDescriptor candidate,
        IReadOnlyList<EvaluationFrameRange> ranges)
    {
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
}
