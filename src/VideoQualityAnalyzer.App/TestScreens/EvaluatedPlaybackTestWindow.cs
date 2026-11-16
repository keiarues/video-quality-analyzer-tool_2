using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using VideoQualityAnalyzer.GpuPreview;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public sealed class EvaluatedPlaybackTestWindow : Window
{
    private readonly ComparisonPanel panel = new("", "", 0, embedded: true);
    private readonly TextBlock status = new();
    public EvaluatedPlaybackTestWindow(string[] paths)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "評価区間のみの再生検証"; Width = 960; Height = 680;
        var root = new DockPanel(); Content = root;
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status); root.Children.Add(panel);
        Closed += (_, _) => panel.Dispose();
        Loaded += async (_, _) =>
        {
            try
            {
                var probe = new FfprobeService(FfprobeService.FindExecutable()!);
                var reference = await probe.ProbeAsync(paths[0], CancellationToken.None);
                var candidate = await probe.ProbeAsync(paths[1], CancellationToken.None);
                var rows = new List<object>();
                foreach (var selection in new[] { new EvaluationSelection(PartialEvaluationMode.TimeRange, 0.2, 0.6),
                    new(PartialEvaluationMode.Periodic, PeriodSeconds: Math.Max(1, Math.Ceiling(reference.Duration.TotalSeconds / 3)), LengthSeconds: 0.2),
                    new(PartialEvaluationMode.Uniform, LengthSeconds: 0.2, Count: 3) })
                {
                    var plan = new EvaluatedPlaybackPlan(selection.WithComparisonTimeline(reference, [candidate]).CreatePlan(reference)!);
                    panel.SetPlaybackRanges(plan.Ranges);
                    await panel.SetSourcesAsync(reference, candidate, TimeSpan.Zero, false);
                    var shown = new List<long>();
                    void Observe() { if (panel.CurrentFrameNumber is { } number) shown.Add(number); }
                    var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    void End() => ended.TrySetResult();
                    panel.FrameShown += Observe; panel.PlaybackEnded += End;
                    panel.Play(); await ended.Task.WaitAsync(TimeSpan.FromSeconds(15));
                    panel.FrameShown -= Observe; panel.PlaybackEnded -= End;
                    if (shown.Count == 0 || shown.Any(number => !plan.Contains(number)) || shown[^1] != plan.LastFrame ||
                        plan.Ranges.Any(range => !shown.Any(number => number >= range.Start && number < range.End)))
                        throw new InvalidDataException("未評価フレームの提示または評価区間の欠落があります。");
                    await panel.VerifyCurrentPixelsAsync();
                    await panel.SeekAsync(TimeSpan.FromSeconds(plan.FirstFrame / reference.AverageFrameRate), false);
                    panel.Play(); await Task.Delay(60); panel.Pause();
                    var paused = panel.CurrentFrameNumber; await Task.Delay(120);
                    if (panel.CurrentFrameNumber != paused) throw new InvalidOperationException("区間再生の一時停止が不正です。");
                    rows.Add(new { Mode = selection.Mode.ToString(), Ranges = plan.Ranges, Shown = shown, Last = plan.LastFrame, Outside = 0, StaticPixels = true, Pause = true });
                    status.Text = selection.Description + "：合格";
                }
                panel.SetPlaybackRanges(null);
                await panel.SeekAsync(TimeSpan.FromSeconds(0.8), false);
                if (panel.CurrentFrameNumber != 24) throw new InvalidOperationException("制限解除後の通常シークが不正です。");
                var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../measurement-results/evaluated-playback"));
                Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(Path.Combine(directory, "dedicated.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
                await File.WriteAllTextAsync(Path.Combine(directory, $"dedicated-{candidate.Codec}.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
                Application.Current.Shutdown(0);
            }
            catch (Exception error) { Console.Error.WriteLine(error); Application.Current.Shutdown(1); }
        };
    }
}
