using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VideoQualityAnalyzer.App;

internal sealed partial class GraphImageSaveWindow
{
    private void QueueRefresh()
    {
        resizeTimer.Stop(); resizeTimer.Start();
    }
    private Size AvailableSize() => new(Math.Max(300, viewport.ActualWidth - 28),
        Math.Max(300, viewport.ActualHeight - 28));
    private void RefreshPreview()
    {
        if (viewport.ActualWidth <= 0 || viewport.ActualHeight <= 0) return;
        try
        {
            preview.Source = CreateImage(); ApplyZoom();
            var bitmap = RequirePreview();
            updatingSizeInputs = true;
            if (!dimension.IsEnabled) dimension.Text = bitmap.PixelWidth.ToString();
            if (!heightDimension.IsEnabled) heightDimension.Text = bitmap.PixelHeight.ToString();
            updatingSizeInputs = false;
            status.Text = $"出力予定の画像：{target.SelectedItem}（{bitmap.PixelWidth} × {bitmap.PixelHeight}）";
        }
        catch (Exception error) { preview.Source = null; status.Text = "プレビューを作成できませんでした：" + error.Message; }
    }
    private BitmapSource RequirePreview() => preview.Source as BitmapSource
        ?? throw new InvalidOperationException("プレビュー画像が生成されていません。" + status.Text);

    private void ApplyZoom()
    {
        if (preview.Source is not BitmapSource bitmap) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var available = AvailableSize();
        displayScale = fitToWindow ? Math.Min(available.Width * dpi.DpiScaleX / bitmap.PixelWidth,
            available.Height * dpi.DpiScaleY / bitmap.PixelHeight) : zoom.Value / 100;
        preview.Width = bitmap.PixelWidth * displayScale / dpi.DpiScaleX;
        preview.Height = bitmap.PixelHeight * displayScale / dpi.DpiScaleY;
        zoomLabel.Text = $"{displayScale * 100:0}%";
        if (fitToWindow)
        {
            updatingZoom = true;
            zoom.Value = Math.Clamp(Math.Round(displayScale * 10) * 10, zoom.Minimum, zoom.Maximum);
            updatingZoom = false;
        }
    }

}
