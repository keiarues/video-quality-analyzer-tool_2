namespace VideoQualityAnalyzer.Media;

// 表示用辞書の参照は維持し、条件の切り替え時だけ内容を入れ替える。
public sealed class EvaluationResultStore
{
    private readonly Dictionary<EvaluationSelection, Dictionary<(string VideoId, QualityMetric Metric), QualityResult>> profiles = [];
    public Dictionary<(string VideoId, QualityMetric Metric), QualityResult> Results { get; } = [];
    public EvaluationSelection Selection { get; private set; } = EvaluationSelection.Full;
    public bool HasResults => Results.Count > 0 || profiles.Values.Any(results => results.Count > 0);

    public void Switch(EvaluationSelection selection)
    {
        if (selection == Selection) { RemoveStale(); return; }
        profiles[Selection] = Results.Where(pair => pair.Value.IsReusableFor(Selection)).ToDictionary(pair => pair.Key, pair => pair.Value);
        Results.Clear();
        if (profiles.TryGetValue(selection, out var saved))
            foreach (var pair in saved.Where(pair => pair.Value.IsReusableFor(selection))) Results.Add(pair.Key, pair.Value);
        Selection = selection;
    }

    private void RemoveStale()
    {
        foreach (var key in Results.Where(pair => !pair.Value.IsReusableFor(Selection)).Select(pair => pair.Key).ToArray()) Results.Remove(key);
    }

    public void Clear() { profiles.Clear(); Results.Clear(); }
    public void RemoveVideo(string videoId)
    {
        foreach (var results in profiles.Values.Append(Results))
            foreach (var key in results.Keys.Where(key => key.VideoId == videoId).ToArray()) results.Remove(key);
    }
}
