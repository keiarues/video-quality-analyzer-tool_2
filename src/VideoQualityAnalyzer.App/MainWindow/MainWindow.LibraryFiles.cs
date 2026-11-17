using System.IO;
using System.Windows;
using Microsoft.Win32;
using VideoQualityAnalyzer.Core;

namespace VideoQualityAnalyzer.App;

public partial class MainWindow
{
    private readonly Dictionary<string, long> loadingVideoPaths = new(StringComparer.OrdinalIgnoreCase);
    private long libraryLoadGeneration;
    private CancellationTokenSource libraryLoadCancellation = new();
    private Func<string, Task>? beforeVideoProbeForVerification;

    private void CancelPendingLibraryLoads()
    {
        libraryLoadGeneration++;
        libraryLoadCancellation.Cancel();
        libraryLoadCancellation.Dispose();
        libraryLoadCancellation = new CancellationTokenSource();
        loadingVideoPaths.Clear();
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "動画を追加",
            Filter = "動画ファイル|*.mkv;*.mp4;*.mov;*.avi;*.webm;*.m4v|すべてのファイル|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) == true)
            await AddFilesAsync(dialog.FileNames);
    }

    private async Task AddFilesAsync(IEnumerable<string> paths, string[]? droppedFolders = null)
    {
        if (probe is null)
        {
            SetWindowStatus("動画を読み込むにはFFmpegとFFprobeが必要です。オプション → 設定でフォルダーを指定し、再起動してください。");
            return;
        }
        if (evaluationCancellation is not null)
        {
            SetWindowStatus("評価中は動画を追加できません。", evaluation: true);
            return;
        }

        var generation = libraryLoadGeneration;
        var cancellationToken = libraryLoadCancellation.Token;
        pendingVideoLoads++;
        try
        {
            foreach (var input in paths)
            {
                if (generation != libraryLoadGeneration) break;
                var path = Path.GetFullPath(input);
                if (videos.Any(item => string.Equals(item.Descriptor.Path, path, StringComparison.OrdinalIgnoreCase)) ||
                    loadingVideoPaths.ContainsKey(path)) continue;
                loadingVideoPaths.Add(path, generation);
                try
                {
                    if (!File.Exists(path))
                    {
                        SetWindowStatus("ファイルが見つかりません: " + Path.GetFileName(path));
                        continue;
                    }

                    SetWindowStatus("動画情報を取得中: " + Path.GetFileName(path));
                    try
                    {
                        if (beforeVideoProbeForVerification is not null)
                            await beforeVideoProbeForVerification(path);
                        if (generation != libraryLoadGeneration) break;
                        var descriptor = await probe.ProbeAsync(path, cancellationToken);
                        if (generation != libraryLoadGeneration) break;
                        var entry = new VideoEntry(descriptor);
                        videos.Add(entry);
                        referenceId ??= descriptor.Id;
                        entry.IsReference = referenceId == descriptor.Id;
                        if (!entry.IsReference && droppedFolders is not null)
                            entry.Group = VideoDropFiles.GetGroup(path, droppedFolders);
                        VideoList.Items.Refresh();
                        log.Record("file_added", "none", descriptor.Id, "ok",
                            detail: $"codec={descriptor.Codec};resolution={descriptor.Width}x{descriptor.Height};fps={descriptor.AverageFrameRate:F3}");
                    }
                    catch (OperationCanceledException) when (generation != libraryLoadGeneration) { break; }
                    catch (Exception exception)
                    {
                        if (generation == libraryLoadGeneration)
                        {
                            SetWindowStatus($"動画情報を取得できません: {Path.GetFileName(path)} ({exception.GetType().Name})");
                            log.Record("probe_failed", "none", null, "failed", detail: exception.GetType().Name);
                        }
                    }
                }
                finally
                {
                    if (loadingVideoPaths.TryGetValue(path, out var reservedGeneration) && reservedGeneration == generation)
                        loadingVideoPaths.Remove(path);
                }
            }

            if (generation != libraryLoadGeneration) return;
            UpdateSelection();
            if (activeSort != VideoSortOrder.Registration) RefreshVideoOrder();
            var groupCount = videos.Where(entry => !entry.IsReference && !string.IsNullOrWhiteSpace(entry.Group))
                .Select(entry => entry.Group).Distinct().Count();
            SetWindowStatus($"読み込み済み：{videos.Count}ファイル／{groupCount}グループ");
        }
        finally { pendingVideoLoads--; }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            e.Handled = true;
            try
            {
                var folders = paths.Where(Directory.Exists).ToArray();
                var files = await Task.Run(() => VideoDropFiles.Expand(paths, () =>
                    Dispatcher.Invoke(() => !closing && MessageBox.Show(this,
                        "読み込む動画が100本以上あります。処理を続けますか？\n「いいえ」を選ぶと今回の登録を中止します。",
                        "動画の一括登録", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                        MessageBoxResult.No) == MessageBoxResult.Yes)));
                if (!closing && files.Length > 0) await AddFilesAsync(files, folders);
            }
            catch (Exception exception)
            {
                SetWindowStatus($"フォルダーを読み込めません: {exception.GetType().Name}");
            }
        }
    }

}
