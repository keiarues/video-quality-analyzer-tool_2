namespace VideoQualityAnalyzer.Core;

public static class VideoDropFiles
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mkv", ".mp4", ".mov", ".avi", ".webm", ".m4v" };

    public static string[] Expand(IEnumerable<string> paths, Func<bool>? confirmLargeImport = null)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var confirmed = false;
        bool Add(string file)
        {
            if (!files.Add(file)) return true;
            if (files.Count < 100 || confirmed || confirmLargeImport is null) return true;
            confirmed = confirmLargeImport();
            return confirmed;
        }
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        foreach (var input in paths)
        {
            var path = Path.GetFullPath(input);
            if (File.Exists(path)) { if (!Add(path)) return []; }
            else if (Directory.Exists(path))
                foreach (var file in Directory.EnumerateFiles(path, "*", options))
                    if (Extensions.Contains(Path.GetExtension(file)) && !Add(file)) return [];
        }
        return files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
