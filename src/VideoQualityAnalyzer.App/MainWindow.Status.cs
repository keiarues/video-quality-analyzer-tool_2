namespace VideoQualityAnalyzer.App;

public partial class MainWindow
{
    // 評価中は進捗表示を優先し、通常操作の通知による上書きを防ぐ。
    private void SetWindowStatus(string message, bool evaluation = false)
    {
        if (!evaluation && evaluationCancellation is not null) return;
        if (!evaluation) WindowStatusText.Foreground = (System.Windows.Media.Brush)FindResource("UiMutedBrush");
        WindowStatusText.Text = message;
    }
}