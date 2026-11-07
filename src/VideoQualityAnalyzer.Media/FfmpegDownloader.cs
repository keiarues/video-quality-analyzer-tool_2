using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace VideoQualityAnalyzer.Media;

public sealed class FfmpegDownloader
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(15) };
    private readonly HttpClient client;
    private readonly string installRoot;
    public FfmpegDownloader(HttpClient? client = null, string? installRoot = null)
    {
        this.client = client ?? Client;
        this.installRoot = installRoot ?? InstallRoot;
    }
    private const string DownloadUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";

    public static string InstallRoot
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "VideoQualityAnalyzer.Wpf.sln")))
                    return Path.Combine(directory.FullName, "tools", "ffmpeg");
            return Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg");
        }
    }

    public async Task<string> DownloadAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(installRoot);
        var staging = Path.Combine(installRoot, ".download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            progress?.Report("最新安定版の情報を取得中…");
            var checksum = await client.GetStringAsync(DownloadUrl + ".sha256", cancellationToken);
            var expected = Regex.Match(checksum, @"\b[0-9a-fA-F]{64}\b").Value;
            if (expected.Length != 64) throw new InvalidDataException("配布元のSHA256を確認できませんでした。");
            var archive = Path.Combine(staging, "download.zip");
            using (var response = await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var output = File.Create(archive);
                var buffer = new byte[81920];
                long total = 0;
                var lastReport = Environment.TickCount64;
                int count;
                while ((count = await input.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    total += count;
                    if (Environment.TickCount64 - lastReport < 200) continue;
                    lastReport = Environment.TickCount64;
                    progress?.Report($"ダウンロード中：{total / 1048576.0:F1} MB" +
                        (response.Content.Headers.ContentLength is long length ? $" / {length / 1048576.0:F1} MB" : ""));
                }
            }
            progress?.Report("整合性を確認中…");
            await using (var stream = File.OpenRead(archive))
            {
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("SHA256が一致しません。時間をおいて再実行してください。");
            }
            progress?.Report("展開・起動確認中…");
            var unpacked = Path.Combine(staging, "unpacked");
            await Task.Run(() => ZipFile.ExtractToDirectory(archive, unpacked), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var executable = Directory.GetFiles(unpacked, "ffmpeg.exe", SearchOption.AllDirectories).Single();
            FfmpegSettings.Resolve(executable);
            var version = await FfmpegSettings.GetVersionAsync(executable, cancellationToken);
            if (!version.StartsWith("ffmpeg version ", StringComparison.Ordinal)) throw new InvalidDataException(version);
            var probeVersion = await FfmpegSettings.GetVersionAsync(Path.Combine(Path.GetDirectoryName(executable)!, "ffprobe.exe"), cancellationToken);
            if (!probeVersion.StartsWith("ffprobe version ", StringComparison.Ordinal)) throw new InvalidDataException(probeVersion);
            cancellationToken.ThrowIfCancellationRequested();
            var packageDirectories = Directory.GetDirectories(unpacked);
            if (packageDirectories.Length != 1 || Directory.GetFiles(unpacked).Length != 0)
                throw new InvalidDataException("配布ZIPのフォルダー構成を確認できませんでした。");
            var package = packageDirectories[0];
            var destination = Path.Combine(installRoot, Path.GetFileName(package));
            if (!Directory.Exists(destination)) Directory.Move(package, destination);
            var installedExecutable = Path.Combine(destination, Path.GetRelativePath(package, executable));
            FfmpegSettings.Resolve(installedExecutable);
            return Path.GetDirectoryName(installedExecutable)!;
        }
        finally
        {
            try { Directory.Delete(staging, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
