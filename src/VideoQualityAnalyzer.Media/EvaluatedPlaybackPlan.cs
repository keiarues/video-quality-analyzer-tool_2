namespace VideoQualityAnalyzer.Media;

// 評価した元フレーム番号の区間。終端を含まず、静止シーク・コマ送り・再生で共用する。
public sealed class EvaluatedPlaybackPlan
{
    public IReadOnlyList<EvaluationFrameRange> Ranges { get; }
    public long FirstFrame => Ranges[0].Start;
    public long LastFrame => Ranges[^1].End - 1;

    public EvaluatedPlaybackPlan(IEnumerable<EvaluationFrameRange> ranges)
    {
        var snapshot = ranges.ToArray();
        if (snapshot.Length == 0 || snapshot.Any(range => range.Start < 0 || range.End <= range.Start) ||
            snapshot.Zip(snapshot.Skip(1)).Any(pair => pair.First.End > pair.Second.Start))
            throw new ArgumentException("再生区間は昇順で、重複しない有効な区間を指定してください。");
        Ranges = Array.AsReadOnly(snapshot);
    }

    public bool Contains(long frame) => Ranges.Any(range => frame >= range.Start && frame < range.End);
    public long ForwardOrLast(long frame)
    {
        foreach (var range in Ranges)
        {
            if (frame < range.Start) return range.Start;
            if (frame < range.End) return frame;
        }
        return LastFrame;
    }
    public long BackwardOrFirst(long frame)
    {
        foreach (var range in Ranges.Reverse())
        {
            if (frame >= range.End) return range.End - 1;
            if (frame >= range.Start) return frame;
        }
        return FirstFrame;
    }
    public long Move(long frame, int direction) => direction < 0 ? BackwardOrFirst(frame - 1) : ForwardOrLast(frame + 1);
}
