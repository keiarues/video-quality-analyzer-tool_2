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

            var candidatePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".mkv");
            var latePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".mkv");
            try
            {
                File.Copy(path, candidatePath);
                File.Copy(path, latePath);
                await AddFilesAsync([candidatePath]);
                var candidate = videos.Single(entry => entry.Descriptor.Path == candidatePath);
                var selectionGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                beforeVideoProbeForVerification = _ => selectionGate.Task;
                var selectionLoad = AddFilesAsync([latePath]);
                VideoList.SelectedItem = candidate;
                selectionGate.SetResult();
                await selectionLoad;
                if (VideoList.SelectedItem != candidate)
                    throw new InvalidOperationException("読み込み中に変更した一覧選択が解除されました。");

                RemoveAll_Click(this, new RoutedEventArgs());
                if (videos.Count != 0) throw new InvalidOperationException("全削除後に動画が残っています。");
                var oldGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var newGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var probeCount = 0;
                beforeVideoProbeForVerification = _ => ++probeCount == 1 ? oldGate.Task : newGate.Task;
                var oldLoad = AddFilesAsync([path]);
                RemoveAll_Click(this, new RoutedEventArgs());
                var newLoad = AddFilesAsync([path]);
                oldGate.SetResult();
                await oldLoad;
                if (videos.Count != 0 || loadingVideoPaths.Count != 1 || pendingVideoLoads != 1)
                    throw new InvalidOperationException("全削除前の読み込み結果が復活したか、新しい予約が解除されました。");
                newGate.SetResult();
                await newLoad;
                if (videos.Count != 1 || loadingVideoPaths.Count != 0 || pendingVideoLoads != 0)
                    throw new InvalidOperationException("全削除後の再登録が不正です。");
            }
            finally
            {
                beforeVideoProbeForVerification = null;
                if (File.Exists(candidatePath)) File.Delete(candidatePath);
                if (File.Exists(latePath)) File.Delete(latePath);
            }
            Console.WriteLine("動画登録: 同時追加、失敗時の予約解除、選択維持、全削除後の古い結果破棄が合格");
            Application.Current.Shutdown(0);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            Application.Current.Shutdown(1);
        }
    }
}
