using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace VideoQualityAnalyzer.App;

public partial class MainWindow
{
    private readonly Dictionary<WheelAction, int> wheelRemainders = [];
    private Task frameShortcutSequence = Task.CompletedTask;

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!initialized || e.Handled) return;
        var area = GetShortcutArea();
        var action = ShortcutInput.ResolveKey(e.Key, Keyboard.Modifiers, area);
        if (action == ShortcutAction.None) return;
        if (action == ShortcutAction.Delete && (VideoList.SelectedItems.Count + ReferenceList.SelectedItems.Count == 0 ||
            evaluationCancellation is not null)) return;
        if (action == ShortcutAction.Open && !AddVideoButton.IsEnabled) return;
        e.Handled = true;
        switch (action)
        {
            case ShortcutAction.PlayPause: PlayPause_Click(this, new RoutedEventArgs()); break;
            case ShortcutAction.BackOne: BackOne_Click(this, new RoutedEventArgs()); break;
            case ShortcutAction.ForwardOne: ForwardOne_Click(this, new RoutedEventArgs()); break;
            case ShortcutAction.BackFive: BackFive_Click(this, new RoutedEventArgs()); break;
            case ShortcutAction.ForwardFive: ForwardFive_Click(this, new RoutedEventArgs()); break;
            case ShortcutAction.FrameBack: QueueFrameShortcut(-1); break;
            case ShortcutAction.FrameForward: QueueFrameShortcut(1); break;
            case ShortcutAction.Mute: MuteCheck.IsChecked = MuteCheck.IsChecked != true; break;
            case ShortcutAction.Open: Add_Click(this, new RoutedEventArgs()); break;
            case ShortcutAction.Delete: Remove_Click(this, new RoutedEventArgs()); break;
        }
    }

    private ShortcutArea GetShortcutArea()
    {
        if (Keyboard.FocusedElement is TextBoxBase or PasswordBox or MenuItem ||
            Keyboard.FocusedElement is ComboBox { IsEditable: true }) return ShortcutArea.TextInput;
        if (VideoList.IsKeyboardFocusWithin || ReferenceList.IsKeyboardFocusWithin) return ShortcutArea.List;
        if ((!previewCollapsed && (InitialPreviewTabs.IsKeyboardFocusWithin || ComparisonHost.IsKeyboardFocusWithin ||
            PlaybackControls.IsKeyboardFocusWithin)) ||
            (!graphsCollapsed && QualityGraphHost.IsKeyboardFocusWithin)) return ShortcutArea.Playback;
        return ShortcutArea.Other;
    }

    private void PreviewSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 映像の空き領域をクリックした後も、一覧のフォーカスを残さない。
        if (e.OriginalSource is Image or MediaElement or Grid or Border &&
            !PreviewLeftSelect.IsKeyboardFocusWithin && !PreviewRightSelect.IsKeyboardFocusWithin)
            ((UIElement)sender).Focus();
    }

    private void SeekSlider_PreviewMouseWheel(object sender, MouseWheelEventArgs e) => HandleShortcutWheel(e, seek: true);
    private void VolumeSlider_PreviewMouseWheel(object sender, MouseWheelEventArgs e) => HandleShortcutWheel(e, volume: true);

    private void HandleShortcutWheel(MouseWheelEventArgs e, bool seek = false, bool graph = false, bool volume = false)
    {
        if (!initialized || e.Handled) return;
        var action = ShortcutInput.ResolveWheel(seek, graph, volume, Keyboard.Modifiers);
        if (action == WheelAction.None || (action != WheelAction.Volume && left.Video is null)) return;
        e.Handled = true;
        var total = wheelRemainders.GetValueOrDefault(action) + e.Delta;
        var steps = total / Mouse.MouseWheelDeltaForOneLine;
        wheelRemainders[action] = total % Mouse.MouseWheelDeltaForOneLine;
        if (steps == 0) return;
        if (action == WheelAction.Volume)
        {
            VolumeSlider.Value = Math.Clamp(VolumeSlider.Value + steps * 0.05, 0, 1);
            return;
        }
        if (seekSliderPointerActive || seekSliderDragging) return;
        if (action == WheelAction.Frame)
        {
            QueueFrameShortcut(steps > 0 ? -1 : 1, Math.Abs(steps));
            return;
        }
        var seconds = action == WheelAction.SeekOne ? 1.0 : 0.2;
        ApplySeek(CurrentPosition() - TimeSpan.FromSeconds(steps * seconds), steps > 0 ? -1 : 1);
    }

    private void QueueFrameShortcut(int direction, int count = 1)
    {
        var previous = frameShortcutSequence;
        frameShortcutSequence = RunFrameShortcutAsync(previous, direction, count);
    }

    private async Task RunFrameShortcutAsync(Task previous, int direction, int count)
    {
        try
        {
            await previous;
            for (var i = 0; i < count && !closing; i++) await MoveFrameAsync(direction);
        }
        catch (Exception exception)
        {
            if (!closing) SetWindowStatus("コマ移動に失敗しました: " + exception.GetType().Name);
        }
    }
}
