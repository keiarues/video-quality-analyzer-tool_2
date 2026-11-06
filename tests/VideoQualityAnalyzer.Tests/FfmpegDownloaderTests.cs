using System.Net;
using System.Security.Cryptography;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.Tests;

[TestClass]
public sealed class FfmpegDownloaderTests
{
    private sealed class DownloadHandler(byte[] archive, bool validHash) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var hash = validHash ? Convert.ToHexString(SHA256.HashData(archive)) : new string('0', 64);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = request.RequestUri!.AbsolutePath.EndsWith(".sha256")
                    ? new StringContent(hash) : new ByteArrayContent(archive)
            });
        }
    }

    [TestMethod]
    public async Task HashMismatchPreservesExistingInstallationAndRemovesTemporaryFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var existing = Path.Combine(root, "既存.txt");
        await File.WriteAllTextAsync(existing, "維持");
        try
        {
            using var client = new HttpClient(new DownloadHandler([1, 2, 3], false));
            await Assert.ThrowsAsync<InvalidDataException>(() => new FfmpegDownloader(client, root).DownloadAsync());
            Assert.AreEqual("維持", await File.ReadAllTextAsync(existing));
            Assert.HasCount(0, Directory.GetDirectories(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task InvalidArchiveIsNotInstalledEvenWithMatchingHash()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            using var client = new HttpClient(new DownloadHandler([1, 2, 3], true));
            await Assert.ThrowsAsync<InvalidDataException>(() => new FfmpegDownloader(client, root).DownloadAsync());
            Assert.HasCount(0, Directory.GetFileSystemEntries(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task CancellationRemovesTemporaryFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            using var client = new HttpClient(new DownloadHandler([], true));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => new FfmpegDownloader(client, root).DownloadAsync(cancellationToken: cancellation.Token));
            Assert.HasCount(0, Directory.GetFileSystemEntries(root));
        }
        finally { Directory.Delete(root, true); }
    }
}
