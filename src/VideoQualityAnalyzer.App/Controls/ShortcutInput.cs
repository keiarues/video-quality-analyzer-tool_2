using System.Windows.Input;

namespace VideoQualityAnalyzer.App;

internal enum ShortcutArea { Other, Playback, List, TextInput }
internal enum ShortcutAction { None, PlayPause, BackOne, ForwardOne, BackFive, ForwardFive,
    FrameBack, FrameForward, Mute, Open, Delete }
internal enum WheelAction { None, SeekShort, SeekOne, Frame, Volume }

internal static class ShortcutInput
{
    internal static ShortcutAction ResolveKey(Key key, ModifierKeys modifiers, ShortcutArea area)
    {
        if (area == ShortcutArea.TextInput) return ShortcutAction.None;
        if (key == Key.Delete && modifiers == ModifierKeys.None && area == ShortcutArea.List)
            return ShortcutAction.Delete;
        if (area == ShortcutArea.Playback)
        {
            if (key == Key.Space && modifiers == ModifierKeys.None) return ShortcutAction.PlayPause;
            if (key is Key.Left or Key.Right)
            {
                var backward = key == Key.Left;
                return modifiers switch
                {
                    ModifierKeys.None => backward ? ShortcutAction.BackOne : ShortcutAction.ForwardOne,
                    ModifierKeys.Shift => backward ? ShortcutAction.BackFive : ShortcutAction.ForwardFive,
                    ModifierKeys.Control => backward ? ShortcutAction.FrameBack : ShortcutAction.FrameForward,
                    _ => ShortcutAction.None
                };
            }
        }
        if (key == Key.O && modifiers == ModifierKeys.Control) return ShortcutAction.Open;
        if (key == Key.M && modifiers == ModifierKeys.Control) return ShortcutAction.Mute;
        return ShortcutAction.None;
    }

    internal static WheelAction ResolveWheel(bool seek, bool graph, bool volume, ModifierKeys modifiers) =>
        volume ? modifiers == ModifierKeys.None ? WheelAction.Volume : WheelAction.None :
        seek || graph ? modifiers switch
        {
            ModifierKeys.None => WheelAction.SeekShort,
            ModifierKeys.Shift => WheelAction.SeekOne,
            ModifierKeys.Control => WheelAction.Frame,
            _ => WheelAction.None
        } : WheelAction.None;
}
