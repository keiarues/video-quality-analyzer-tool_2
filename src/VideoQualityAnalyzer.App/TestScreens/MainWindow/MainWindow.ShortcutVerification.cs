using System.Windows;
using System.Windows.Input;

namespace VideoQualityAnalyzer.App;

public partial class MainWindow
{
    private async Task VerifyMainShortcutsAsync()
    {
        try
        {
            void Check(UIElement element, ShortcutArea expected)
            {
                if (!element.Focus() || GetShortcutArea() != expected)
                    throw new InvalidOperationException($"{element.GetType().Name} のフォーカス領域が {expected} になりません。");
            }
            Check(comparisonActive ? ComparisonHost : LegacyPreviewGrid, ShortcutArea.Playback);
            Check(SeekSlider, ShortcutArea.Playback);
            Check(PlayPauseButton, ShortcutArea.Playback);
            Check(QualityGraphHost, ShortcutArea.Playback);
            Check(VideoList, ShortcutArea.List);
            Check(ReferenceList, ShortcutArea.List);
            Check(AddVideoButton, ShortcutArea.Other);
            if (ShortcutInput.ResolveKey(Key.Delete, ModifierKeys.None, GetShortcutArea()) != ShortcutAction.None)
                throw new InvalidOperationException("一覧外でDelが有効です。");
            Console.WriteLine("主画面ショートカット: プレビュー・再生UI・グラフ・両一覧のフォーカス判定が合格");
            if (videos.Count >= 2)
            {
                VideoList.SelectedItem = videos.First(entry => !entry.IsReference);
                await comparisonChangeTask;
                ApplySeek(TimeSpan.FromSeconds(5));
                await comparisonChangeTask;
                KeyEventArgs Press(Key key)
                {
                    var args = new KeyEventArgs(InputManager.Current.PrimaryKeyboardDevice,
                        PresentationSource.FromVisual(this), 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                    Window_PreviewKeyDown(this, args);
                    return args;
                }
                Check(PlayPauseButton, ShortcutArea.Playback);
                if (!Press(Key.Space).Handled || !isPlaying) throw new InvalidOperationException("Spaceで再生できません。");
                if (!Press(Key.Space).Handled || isPlaying) throw new InvalidOperationException("Spaceで一時停止できません。");
                var before = CurrentPosition();
                if (!Press(Key.Left).Handled || Math.Abs((CurrentPosition() - (before - TimeSpan.FromSeconds(1))).TotalSeconds) > 0.2)
                    throw new InvalidOperationException("左矢印で1秒戻れません。");
                var count = videos.Count;
                Check(AddVideoButton, ShortcutArea.Other);
                if (Press(Key.Delete).Handled || videos.Count != count)
                    throw new InvalidOperationException("一覧外のDelで動画が削除されました。");
                Check(VideoList, ShortcutArea.List);
                if (!Press(Key.Delete).Handled || videos.Count != count - 1)
                    throw new InvalidOperationException("比較動画一覧のDelで選択動画を削除できません。");
                Console.WriteLine("主画面ショートカット: Space・左矢印・一覧外／一覧内Delが合格");
            }
            Application.Current.Shutdown(0);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            Application.Current.Shutdown(1);
        }
    }
}
