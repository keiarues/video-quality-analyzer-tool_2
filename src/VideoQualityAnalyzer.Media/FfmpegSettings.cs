using System.IO;

namespace VideoQualityAnalyzer.Media;

public static class FfmpegSettings
{
    public static string ActivePath { get; } = Load();
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoQualityAnalyzer", "ffmpeg-path.txt");
    public static string Load()
    {
        try { return File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath).Trim() : ""; }
        catch (IOException) { return ""; }
        catch (UnauthorizedAccessException) { return ""; }
    }
    public static string Resolve(string input)
    {
        var path = Path.GetFullPath(input.Trim().Trim('"'));
        if (Directory.Exists(path)) path = Path.Combine(path, "ffmpeg.exe");
        if (!File.Exists(path) || !Path.GetFileName(path).Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("ffmpeg.exe、またはそれを含むフォルダーを指定してください。");
        if (!File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "ffprobe.exe")))
            throw new ArgumentException("同じフォルダーにffprobe.exeも配置してください。");
        return path;
    }
    public static void Save(string input)
    {
        var path = string.IsNullOrWhiteSpace(input) ? "" : Resolve(input);
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, path);
    }
}

