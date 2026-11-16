namespace VideoQualityAnalyzer.App;

internal static class QualityGraphScale
{
    public static (double Minimum, double Maximum) VerticalRange(IEnumerable<double> values)
    {
        var finite = values.Where(double.IsFinite).ToArray();
        if (finite.Length == 0) return (0, 1);
        var minimum = finite.Min();
        var maximum = finite.Max();
        // 点や折れ線が上下端に重ならないよう余白を付け、単一値でも範囲を確保する。
        var padding = Math.Max((maximum - minimum) * 0.05,
            Math.Max(Math.Max(Math.Abs(minimum), Math.Abs(maximum)) * 0.001, 0.000001));
        return (minimum - padding, maximum + padding);
    }
}
