namespace VideoQualityAnalyzer.Media;

// 表示用辞書の参照は維持し、条件の切り替え時だけ内容を入れ替える。
public sealed class EvaluationResultStore
{
    private readonly Dictionary<EvaluationSelection, Dictionary<(string VideoId, QualityMetric Metric), QualityResult>> profiles = [];
    public Dictionary<(string VideoId, QualityMetric Metric), QualityResult> Results { get; } = [];
    public EvaluationSelection Selection { get; private set; } = EvaluationSelection.Full;

    public void Switch(EvaluationSelection selection)
    {
        if (selection == Selection) return;
        profiles[Selection] = new(Results);
        Results.Clear();
        if (profiles.TryGetValue(selection, out var saved))
            foreach (var pair in saved) Results.Add(pair.Key, pair.Value);
        Selection = selection;
    }

    public void Clear() { profiles.Clear(); Results.Clear(); }
    public void RemoveVideo(string videoId)
    {
        foreach (var results in profiles.Values.Append(Results))
            foreach (var key in results.Keys.Where(key => key.VideoId == videoId).ToArray()) results.Remove(key);
    }
}
