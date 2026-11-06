using System.IO;

namespace VideoQualityAnalyzer.Media;

public static class FfmpegSettings
{
    public static string ActivePath { get; } = Detect(Load()).Path ?? "";
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoQualityAnalyzer", "ffmpeg-path.txt");
    public static string Load()
    {
        try { return File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath).Trim() : ""; }
        catch (IOException) { return ""; }
        catch (UnauthorizedAccessException) { return ""; }
    }
    public static string Resolve(string input)
    {
        var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(input.Trim().Trim('"')));
        if (Directory.Exists(path)) path = Path.Combine(path, "ffmpeg.exe");
        if (!File.Exists(path) || !Path.GetFileName(path).Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("ffmpeg.exeを含むフォルダーを指定してください。");
        if (!File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "ffprobe.exe")))
            throw new ArgumentException("同じフォルダーにffprobe.exeも配置してください。");
        return path;
    }
    public static (string? Path, string Source) Detect(string input)
    {
        if (!string.IsNullOrWhiteSpace(input))
        {
            try { return (Resolve(input), "指定フォルダー"); }
            catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
            { return (null, error.Message); }
        }
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var tools = Path.Combine(directory.FullName, "tools");
            if (!Directory.Exists(tools)) continue;
            try
            {
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                foreach (var candidate in Directory.EnumerateFiles(tools, "ffmpeg.exe", options).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                    if (File.Exists(Path.Combine(Path.GetDirectoryName(candidate)!, "ffprobe.exe"))) return (candidate, "tools内の自動検出");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try { return (Resolve(directory), "PATHからの自動検出"); }
            catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { }
        }
        return (null, "未検出：ffmpeg.exeとffprobe.exeを含むフォルダーを指定してください。");
    }
    public static void Save(string input)
    {
        var path = string.IsNullOrWhiteSpace(input) ? "" : Path.GetDirectoryName(Resolve(input))!;
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, path);
    }
}
