namespace VideoQualityAnalyzer.Media;

public sealed record EvaluationFrameDigest(long Number, long Bytes, string Sha256);
public sealed record InputSeekProgress(string Stage, int Interval, int Intervals, long CompletedFrames, long TotalFrames, TimeSpan SourceTime);
public sealed record InputSeekRun(QualityResult Result, IReadOnlyList<EvaluationFrameDigest> ReferenceFrames,
    IReadOnlyList<EvaluationFrameDigest> CandidateFrames, TimeSpan Elapsed);
public sealed record InputSeekComparison(InputSeekRun Sequential, InputSeekRun Seek,
    int ReferencePixelMismatches, int CandidatePixelMismatches, int ValueMismatches, double MaximumValueDifference)
{
    public bool Passed => ReferencePixelMismatches == 0 && CandidatePixelMismatches == 0 && ValueMismatches == 0;

    public static InputSeekComparison Create(InputSeekRun sequential, InputSeekRun seek)
    {
        static int PixelMismatches(IReadOnlyList<EvaluationFrameDigest> expected, IReadOnlyList<EvaluationFrameDigest> actual) =>
            Math.Abs(expected.Count - actual.Count) + expected.Zip(actual).Count(pair => pair.First != pair.Second);

        var expectedValues = sequential.Result.Frames.ToDictionary(frame => frame.Number, frame => frame.Value);
        var actualValues = seek.Result.Frames.ToDictionary(frame => frame.Number, frame => frame.Value);
        var differences = expectedValues.Keys.Union(actualValues.Keys).Select(number =>
            expectedValues.TryGetValue(number, out var expected) && actualValues.TryGetValue(number, out var actual)
                ? Math.Abs(expected - actual) : double.PositiveInfinity).ToArray();
        return new(sequential, seek, PixelMismatches(sequential.ReferenceFrames, seek.ReferenceFrames),
            PixelMismatches(sequential.CandidateFrames, seek.CandidateFrames),
            differences.Count(value => value > 0.000001), differences.DefaultIfEmpty(0).Max());
    }
}
