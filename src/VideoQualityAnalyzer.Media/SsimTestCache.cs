using System.Text.Json;
using VideoQualityAnalyzer.Core;

namespace VideoQualityAnalyzer.Media;

public sealed record TestVideoStamp(string Path, long Length, long LastWriteTicks)
{
    public static TestVideoStamp Read(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists) throw new IOException("動画が見つかりません。");
        return new(file.FullName, file.Length, file.LastWriteTimeUtc.Ticks);
    }
}

public sealed record SsimTestEntry(VideoDescriptor Video, TestVideoStamp Stamp, QualityResult Result);
public sealed record SsimTestCache(int Version, VideoDescriptor Reference, TestVideoStamp ReferenceStamp,
    SsimTestEntry[] Entries)
{
    public static async Task<SsimTestCache> LoadAsync(string path)
    {
        var cache = JsonSerializer.Deserialize<SsimTestCache>(await File.ReadAllTextAsync(path))
            ?? throw new InvalidDataException("SSIM保存結果が空です。");
        if (cache.Version != 1 || cache.Entries.Length == 0)
            throw new InvalidDataException("SSIM保存形式が未対応、または結果がありません。");
        if (TestVideoStamp.Read(cache.Reference.Path) != cache.ReferenceStamp)
            throw new InvalidDataException("基準動画が変更されています。SSIMを再準備してください。");
        foreach (var entry in cache.Entries)
        {
            if (TestVideoStamp.Read(entry.Video.Path) != entry.Stamp)
                throw new InvalidDataException("比較動画が変更されています。SSIMを再準備してください。");
            if (entry.Result.Metric != QualityMetric.Ssim || entry.Result.Frames.Count == 0 ||
                !double.IsFinite(entry.Result.Average) || entry.Result.Frames.Any(f => !double.IsFinite(f.Value)))
                throw new InvalidDataException("SSIM保存結果が不正です。");
        }
        return cache;
    }

    public async Task SaveAsync(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(this));
        File.Move(temporary, path, true);
    }
}
