using System.IO;

namespace VideoQualityAnalyzer.Media;

public static class FfmpegSettings
{
    public static async Task<string> GetVersionAsync(string executable, CancellationToken cancellationToken = default)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo(executable)
            {
                Arguments = "-version", UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            }
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var text = await output;
            await error;
            if (process.ExitCode != 0) return "バージョン取得失敗";
            return text.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.StartsWith(Path.GetFileNameWithoutExtension(executable) + " version ", StringComparison.Ordinal))
                ?? "バージョン情報を確認できませんでした。";
        }
        catch (OperationCanceledException) { return "バージョン取得を中断しました。"; }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        { return "バージョン取得失敗：" + error.Message; }
        finally { EvaluationProcess.Kill(process); }
    }
    public static string ActivePath { get; } = Detect(Load()).Path ?? "";
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoQualityAnalyzer", "settings.txt");
    private static string LegacySettingsPath => Path.Combine(Path.GetDirectoryName(SettingsPath)!, "ffmpeg-path.txt");
    public static string Load()
    {
        try
        {
            // 新しい設定ファイルがない場合は従来の設定を引き継ぐ。
            var path = File.Exists(SettingsPath) ? SettingsPath : LegacySettingsPath;
            return File.Exists(path) ? File.ReadAllText(path).Trim() : "";
        }
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
