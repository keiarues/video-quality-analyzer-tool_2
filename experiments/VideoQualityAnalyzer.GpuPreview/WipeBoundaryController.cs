using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VideoQualityAnalyzer.GpuPreview;

// 映像の表示範囲を基準にワイプ境界を動かし、切り抜きと操作線を同じ座標に置く。
public sealed class WipeBoundaryController
{
    private readonly FrameworkElement viewport;
    private readonly FrameworkElement rightLayer;
    private readonly FrameworkElement handle;
    private readonly TextBlock positionText;
    private readonly Func<double> aspectProvider;
    private bool dragging;
    private double fraction = 0.5;

    public WipeBoundaryController(FrameworkElement viewport, FrameworkElement rightLayer,
        FrameworkElement handle, TextBlock positionText, Func<double> aspectProvider)
    {
        this.viewport = viewport;
        this.rightLayer = rightLayer;
        this.handle = handle;
        this.positionText = positionText;
        this.aspectProvider = aspectProvider;
        viewport.SizeChanged += (_, _) => Update();
        handle.MouseLeftButtonDown += HandleMouseDown;
        handle.MouseMove += HandleMouseMove;
        handle.MouseLeftButtonUp += HandleMouseUp;
        handle.LostMouseCapture += (_, _) => dragging = false;
        Update();
    }

    public double Fraction => fraction;
    public double ImageLeft { get; private set; }
    public double ImageWidth { get; private set; }

    public void SetFraction(double value)
    {
        fraction = Math.Clamp(value, 0, 1);
        Update();
    }

    public void SetFromViewportX(double x)
    {
        UpdateImageBounds();
        if (ImageWidth <= 0) return;
        SetFraction((x - ImageLeft) / ImageWidth);
    }

    public void Update()
    {
        UpdateImageBounds();
        var boundary = ImageLeft + ImageWidth * fraction;
        rightLayer.Clip = new RectangleGeometry(new Rect(boundary, 0,
            Math.Max(0, viewport.ActualWidth - boundary), viewport.ActualHeight));
        handle.Margin = new Thickness(boundary - handle.Width / 2, 0, 0, 0);
        positionText.Text = $"境界 {fraction * 100:F0}%（左側: 左動画 / 右側: 右動画）";
    }

    private void UpdateImageBounds()
    {
        var width = viewport.ActualWidth;
        var height = viewport.ActualHeight;
        var aspect = aspectProvider();
        ImageWidth = width > 0 && height > 0 && aspect > 0 ? Math.Min(width, height * aspect) : 0;
        ImageLeft = (width - ImageWidth) / 2;
    }

    private void HandleMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!handle.CaptureMouse()) return;
        dragging = true;
        SetFromViewportX(e.GetPosition(viewport).X);
        e.Handled = true;
    }

    private void HandleMouseMove(object sender, MouseEventArgs e)
    {
        if (!dragging) return;
        SetFromViewportX(e.GetPosition(viewport).X);
        e.Handled = true;
    }

    private void HandleMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!dragging) return;
        SetFromViewportX(e.GetPosition(viewport).X);
        dragging = false;
        handle.ReleaseMouseCapture();
        e.Handled = true;
    }
}
