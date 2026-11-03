namespace VideoQualityAnalyzer.Core;

public sealed class VideoEntry(VideoDescriptor descriptor)
{
    private static long nextOrder;
    public VideoDescriptor Descriptor { get; } = descriptor;
    public long RegistrationOrder { get; } = Interlocked.Increment(ref nextOrder);
    public bool IsReference { get; set; }
    public string Group { get; set; } = "";
    public override string ToString() => (IsReference ? "【元動画】 " : "") + Descriptor.Name +
        (Group.Length > 0 ? $" [{Group}]" : "");
}

public enum VideoSortOrder { Registration, Name, Group }

public static class VideoLibrary
{
    public static void SetGroup(IEnumerable<VideoEntry> entries, string group)
    {
        foreach (var entry in entries.Where(entry => !entry.IsReference)) entry.Group = group.Trim();
    }

    public static VideoEntry[] Sort(IEnumerable<VideoEntry> entries, VideoSortOrder order) => order switch
    {
        VideoSortOrder.Name => entries.OrderBy(entry => entry.Descriptor.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(),
        VideoSortOrder.Group => entries.OrderBy(entry => entry.Group, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(entry => entry.Descriptor.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(),
        _ => entries.OrderBy(entry => entry.RegistrationOrder).ToArray()
    };
}
