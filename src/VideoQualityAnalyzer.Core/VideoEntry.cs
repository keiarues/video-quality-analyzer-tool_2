namespace VideoQualityAnalyzer.Core;

public sealed class VideoEntry(VideoDescriptor descriptor)
{
    private static long nextOrder;
    public VideoDescriptor Descriptor { get; } = descriptor;
    public long RegistrationOrder { get; } = Interlocked.Increment(ref nextOrder);
    public bool IsReference { get; set; }
    public string Group { get; set; } = "";
    public string DisplayName => Descriptor.Name;
    public string Resolution => $"{Descriptor.Width}×{Descriptor.Height}";
    public string FrameRateText => $"{Descriptor.AverageFrameRate:F3}" + (Descriptor.IsVariableFrameRate ? "（可変）" : "");
    public double? AverageBitrateKbps => Descriptor.AverageBitrate / 1_000d;
    public double? AverageBitrateMbps => Descriptor.AverageBitrate / 1_000_000d;
    public double? VmafAverage { get; set; }
    public double? SsimAverage { get; set; }
    public double? PsnrAverage { get; set; }
    public override string ToString() => Descriptor.Name;
}

public enum VideoSortOrder { Registration, Name, Group, Resolution, FrameRate, Codec, Bitrate, Vmaf, Ssim, Psnr }

public static class VideoLibrary
{
    public static void SetGroup(IEnumerable<VideoEntry> entries, string group)
    {
        foreach (var entry in entries.Where(entry => !entry.IsReference)) entry.Group = group.Trim();
    }

    public static VideoEntry[] Sort(IEnumerable<VideoEntry> entries, VideoSortOrder order, bool descending = false)
    {
        Func<VideoEntry, IComparable?> key = order switch
        {
            VideoSortOrder.Name => entry => entry.Descriptor.Name,
            VideoSortOrder.Group => entry => entry.Group,
            VideoSortOrder.Resolution => entry => (long)entry.Descriptor.Width * entry.Descriptor.Height,
            VideoSortOrder.FrameRate => entry => entry.Descriptor.AverageFrameRate,
            VideoSortOrder.Codec => entry => entry.Descriptor.Codec,
            VideoSortOrder.Bitrate => entry => entry.Descriptor.AverageBitrate,
            VideoSortOrder.Vmaf => entry => entry.VmafAverage,
            VideoSortOrder.Ssim => entry => entry.SsimAverage,
            VideoSortOrder.Psnr => entry => entry.PsnrAverage,
            _ => entry => entry.RegistrationOrder
        };
        var comparer = Comparer<IComparable?>.Create((left, right) => left is string a && right is string b
            ? StringComparer.CurrentCultureIgnoreCase.Compare(a, b) : Comparer<IComparable?>.Default.Compare(left, right));
        // 未評価値は昇順・降順とも末尾に置き、評価済みの値を数値として並べる。
        var present = entries.Where(entry => key(entry) is not null);
        var ordered = descending ? present.OrderByDescending(key, comparer) : present.OrderBy(key, comparer);
        return ordered.ThenBy(entry => entry.RegistrationOrder)
            .Concat(entries.Where(entry => key(entry) is null).OrderBy(entry => entry.RegistrationOrder)).ToArray();
    }
}
