namespace VideoQualityAnalyzer.Core;

public sealed record PreviewSelection(string? LeftVideoId, string? RightVideoId, long Generation);

public static class SelectionService
{
    public static PreviewSelection Resolve(
        IReadOnlyList<VideoDescriptor> videos,
        string? referenceId,
        IReadOnlyCollection<string> selectedIds,
        string? previousRightId,
        long generation)
    {
        if (videos.Count == 0)
            return new PreviewSelection(null, null, generation);

        var reference = videos.FirstOrDefault(video => video.Id == referenceId) ?? videos[0];
        var selected = videos.Where(video => selectedIds.Contains(video.Id)).ToArray();
        if (selected.Length >= 2)
            return new PreviewSelection(selected[0].Id, selected[1].Id, generation);

        if (selected.Length == 1 && selected[0].Id != reference.Id)
            return new PreviewSelection(reference.Id, selected[0].Id, generation);

        var right = videos.FirstOrDefault(video => video.Id == previousRightId && video.Id != reference.Id)
            ?? videos.FirstOrDefault(video => video.Id != reference.Id);
        return new PreviewSelection(reference.Id, right?.Id, generation);
    }
}
