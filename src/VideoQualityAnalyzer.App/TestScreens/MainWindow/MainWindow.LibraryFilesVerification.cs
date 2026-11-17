using System.IO;
using System.Windows;

namespace VideoQualityAnalyzer.App;

public partial class MainWindow
{
    private async Task VerifyConcurrentAddAsync(string[] paths)
    {
        try
        {
            if (paths.Length != 1) throw new InvalidOperationException("検証用の動画を1本指定してください。");
            var path = Path.GetFullPath(paths[0]);
            var first = AddFilesAsync([path]);
            var second = AddFilesAsync([path]);
            await Task.WhenAll(first, second);
            if (videos.Count(entry => string.Equals(entry.Descriptor.Path, path, StringComparison.OrdinalIgnoreCase)) != 1 ||
                loadingVideoPaths.Count != 0 || pendingVideoLoads != 0)
                throw new InvalidOperationException("同時追加で同じ動画が重複登録されました。");

            var missing = Path.Combine(Path.GetDirectoryName(path)!, Guid.NewGuid().ToString("N") + ".mkv");
            await AddFilesAsync([missing]);
            if (loadingVideoPaths.Count != 0 || pendingVideoLoads != 0)
                throw new InvalidOperationException("読み込み失敗後にパスの予約が残っています。");

            var invalid = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".mkv");
            try
            {
                await File.WriteAllTextAsync(invalid, "invalid video");
                await AddFilesAsync([invalid]);
                if (loadingVideoPaths.Count != 0 || pendingVideoLoads != 0 ||
                    videos.Any(entry => string.Equals(entry.Descriptor.Path, invalid, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("FFprobe失敗後の予約解除が不正です。");
            }
            finally { if (File.Exists(invalid)) File.Delete(invalid); }
            Console.WriteLine("動画登録: 同時追加の重複防止と読み込み失敗後の予約解除が合格");
            Application.Current.Shutdown(0);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            Application.Current.Shutdown(1);
        }
    }
}
