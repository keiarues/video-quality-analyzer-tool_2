using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VideoQualityAnalyzer.App;

// 本体へ組み込む前に、キーとフォーカスの組み合わせを専用画面で確認する。
internal sealed class ShortcutTestWindow : Window
{
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private readonly Border preview = new() { Focusable = true, Padding = new Thickness(8) };
    private readonly Slider seek = new() { Minimum = 0, Maximum = 10, Value = 5 };
    private readonly Border graph = new() { Focusable = true, Padding = new Thickness(8) };
    private readonly DataGrid list = new() { Height = 70, AutoGenerateColumns = true, IsReadOnly = true };
    private readonly TextBox input = new() { Text = "文字入力" };
    private readonly Button outside = new() { Content = "対象外のボタン" };

    internal ShortcutTestWindow(bool automatic)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "ショートカット操作の検証";
        Width = 720;
        Height = 430;
        var panel = new StackPanel { Margin = new Thickness(16) };
        Content = panel;
        panel.Children.Add(new TextBlock { Text = "キー操作とフォーカスの優先順位を検証します。" });
        preview.Child = new TextBlock { Text = "プレビュー領域（クリックしてフォーカス）" };
        preview.MouseLeftButtonDown += (_, _) => preview.Focus();
        graph.Child = new TextBlock { Text = "グラフ領域（クリックしてフォーカス）" };
        graph.MouseLeftButtonDown += (_, _) => graph.Focus();
        list.ItemsSource = new[] { new { 動画 = "比較A" }, new { 動画 = "比較B" } };
        foreach (var element in new FrameworkElement[] { preview, seek, graph, list, input, outside })
        {
            element.Margin = new Thickness(4);
            panel.Children.Add(element);
        }
        PreviewKeyDown += (_, e) =>
        {
            var area = CurrentArea();
            var action = ShortcutInput.ResolveKey(e.Key, Keyboard.Modifiers, area);
            status.Text = $"領域: {area}／操作: {action}";
            if (action != ShortcutAction.None) e.Handled = true;
        };
        panel.Children.Add(status);
        if (automatic) Loaded += (_, _) => Verify();
    }

    private ShortcutArea CurrentArea() =>
        input.IsKeyboardFocusWithin ? ShortcutArea.TextInput :
        list.IsKeyboardFocusWithin ? ShortcutArea.List :
        preview.IsKeyboardFocusWithin || seek.IsKeyboardFocusWithin || graph.IsKeyboardFocusWithin
            ? ShortcutArea.Playback : ShortcutArea.Other;

    private void Verify()
    {
        void Check(Key key, ModifierKeys modifiers, ShortcutArea area, ShortcutAction expected)
        {
            var actual = ShortcutInput.ResolveKey(key, modifiers, area);
            if (actual != expected) throw new InvalidOperationException($"{area} {modifiers}+{key}: {actual} != {expected}");
        }
        Check(Key.Space, ModifierKeys.None, ShortcutArea.Playback, ShortcutAction.PlayPause);
        Check(Key.Space, ModifierKeys.None, ShortcutArea.List, ShortcutAction.None);
        Check(Key.Space, ModifierKeys.None, ShortcutArea.TextInput, ShortcutAction.None);
        Check(Key.Left, ModifierKeys.None, ShortcutArea.Playback, ShortcutAction.BackOne);
        Check(Key.Right, ModifierKeys.Shift, ShortcutArea.Playback, ShortcutAction.ForwardFive);
        Check(Key.Left, ModifierKeys.Control, ShortcutArea.Playback, ShortcutAction.FrameBack);
        Check(Key.Up, ModifierKeys.None, ShortcutArea.Playback, ShortcutAction.None);
        Check(Key.Delete, ModifierKeys.None, ShortcutArea.List, ShortcutAction.Delete);
        Check(Key.Delete, ModifierKeys.None, ShortcutArea.Playback, ShortcutAction.None);
        Check(Key.O, ModifierKeys.Control, ShortcutArea.Other, ShortcutAction.Open);
        Check(Key.M, ModifierKeys.Control, ShortcutArea.TextInput, ShortcutAction.None);
        void CheckFocus(UIElement element, ShortcutArea expected)
        {
            if (!element.Focus() || CurrentArea() != expected)
                throw new InvalidOperationException($"{element.GetType().Name} のフォーカス領域が {expected} になりません。");
        }
        CheckFocus(preview, ShortcutArea.Playback);
        CheckFocus(seek, ShortcutArea.Playback);
        CheckFocus(graph, ShortcutArea.Playback);
        CheckFocus(list, ShortcutArea.List);
        CheckFocus(input, ShortcutArea.TextInput);
        CheckFocus(outside, ShortcutArea.Other);
        if (ShortcutInput.ResolveWheel(true, false, false, ModifierKeys.None) != WheelAction.SeekShort ||
            ShortcutInput.ResolveWheel(true, false, false, ModifierKeys.Shift) != WheelAction.SeekOne ||
            ShortcutInput.ResolveWheel(true, false, false, ModifierKeys.Control) != WheelAction.Frame ||
            ShortcutInput.ResolveWheel(false, true, false, ModifierKeys.Control) != WheelAction.Frame ||
            ShortcutInput.ResolveWheel(false, false, true, ModifierKeys.None) != WheelAction.Volume ||
            ShortcutInput.ResolveWheel(false, false, true, ModifierKeys.Control) != WheelAction.None ||
            ShortcutInput.ResolveWheel(false, false, false, ModifierKeys.None) != WheelAction.None)
            throw new InvalidOperationException("ホイールの対象領域が正しくありません。");
        Console.WriteLine("ショートカット専用画面: キー・フォーカス・ホイール判定が合格");
        Application.Current.Shutdown();
    }
}
