using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VideoQualityAnalyzer.GpuPreview;

public partial class ComparisonPanel
{
    // 再生とフレーム選択は既存の共通処理を使い、各比較表示で倍率と位置を共有する。
    public void MoveZoomControlsTo(Panel host)
    {
        if (ZoomControls.Parent is Panel parent) parent.Children.Remove(ZoomControls);
        host.Children.Add(ZoomControls);
        ZoomControls.Margin = new Thickness(0, 4, 0, 0);
    }

    public void EnableZoomTest() => EnableZoom(true);

    public void EnableZoom(bool testMode = false, ZoomControlBar? controls = null)
    {
        {
            // Canvas上で配置し、拡大画像のレイアウト切り抜きが移動量に影響しないようにする。
            foreach (var image in new[] { WipeLeftImage, WipeRightImage })
            {
                var parent = (Grid)image.Parent;
                var index = parent.Children.IndexOf(image);
                parent.Children.Remove(image);
                var canvas = new Canvas();
                canvas.Children.Add(image);
                parent.Children.Insert(index, canvas);
            }
        }
        var images = new[] { LeftImage, RightImage, HeatmapImage };
        var viewers = new List<ScrollViewer>();
        var fit = true;
        var updating = false;
        var scale = 1.0;
        var x = 0.0;
        var y = 0.0;
        var externalControls = controls is not null;
        controls ??= new ZoomControlBar();
        var label = controls.ScaleLabel;
        void Apply()
        {
            updating = true;
            var dpi = VisualTreeHelper.GetDpi(this);
            for (var i = 0; i < images.Length; i++)
            {
                var image = images[i];
                if (fit)
                {
                    image.Width = double.NaN;
                    image.Height = double.NaN;
                    image.Stretch = Stretch.Uniform;
                    viewers[i].HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                    viewers[i].VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                }
                else if (image.Source is BitmapSource bitmap)
                {
                    viewers[i].HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                    viewers[i].VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                    image.Stretch = Stretch.Fill;
                    image.Width = bitmap.PixelWidth * scale / dpi.DpiScaleX;
                    image.Height = bitmap.PixelHeight * scale / dpi.DpiScaleY;
                    viewers[i].ScrollToHorizontalOffset(x / dpi.DpiScaleX);
                    viewers[i].ScrollToVerticalOffset(y / dpi.DpiScaleY);
                }
            }
            {
                wipeBoundaryController.UseViewportBounds = !fit;
                wipeBoundaryController.Update();
                foreach (var image in new[] { WipeLeftImage, WipeRightImage })
                {
                    if (fit)
                    {
                        image.Width = Math.Max(0, WipeViewport.ActualWidth);
                        image.Height = Math.Max(0, WipeViewport.ActualHeight);
                        image.HorizontalAlignment = HorizontalAlignment.Stretch;
                        image.VerticalAlignment = VerticalAlignment.Stretch;
                        image.Stretch = Stretch.Uniform;
                        image.RenderTransform = Transform.Identity;
                    }
                    else if (image.Source is BitmapSource bitmap)
                    {
                        image.HorizontalAlignment = HorizontalAlignment.Left;
                        image.VerticalAlignment = VerticalAlignment.Top;
                        image.Width = bitmap.PixelWidth * scale / dpi.DpiScaleX;
                        image.Height = bitmap.PixelHeight * scale / dpi.DpiScaleY;
                        image.Stretch = Stretch.Fill;
                        var offsetX = Math.Clamp(x / dpi.DpiScaleX, 0, Math.Max(0, image.Width - WipeViewport.ActualWidth));
                        var offsetY = Math.Clamp(y / dpi.DpiScaleY, 0, Math.Max(0, image.Height - WipeViewport.ActualHeight));
                        image.RenderTransform = new TranslateTransform(Math.Max(0, (WipeViewport.ActualWidth - image.Width) / 2) - offsetX,
                            Math.Max(0, (WipeViewport.ActualHeight - image.Height) / 2) - offsetY);
                    }
                }
            }
            label.Text = fit ? "自動" : $"{scale * 100:0}%";
            updating = false;
        }
        foreach (var image in images)
        {
            var border = (Border)image.Parent;
            border.Child = null;
            var viewer = new ScrollViewer { Content = image, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            border.Child = viewer;
            viewers.Add(viewer);
            Point? dragStart = null;
            double startX = 0, startY = 0;
            image.Cursor = System.Windows.Input.Cursors.Hand;
            image.MouseLeftButtonDown += (_, e) =>
            {
                if (fit) return;
                dragStart = e.GetPosition(viewer);
                startX = x;
                startY = y;
                image.CaptureMouse();
                e.Handled = true;
            };
            image.MouseMove += (_, e) =>
            {
                if (dragStart is not Point start || !image.IsMouseCaptured) return;
                var position = e.GetPosition(viewer);
                var dpi = VisualTreeHelper.GetDpi(this);
                x = Math.Max(0, startX - (position.X - start.X) * dpi.DpiScaleX);
                y = Math.Max(0, startY - (position.Y - start.Y) * dpi.DpiScaleY);
                Apply();
                e.Handled = true;
            };
            image.MouseLeftButtonUp += (_, e) =>
            {
                if (dragStart is null) return;
                dragStart = null;
                image.ReleaseMouseCapture();
                e.Handled = true;
            };
            image.LostMouseCapture += (_, _) => dragStart = null;
            viewer.ScrollChanged += (_, e) =>
            {
                if (updating || fit || (e.HorizontalChange == 0 && e.VerticalChange == 0)) return;
                var dpi = VisualTreeHelper.GetDpi(this);
                x = viewer.HorizontalOffset * dpi.DpiScaleX;
                y = viewer.VerticalOffset * dpi.DpiScaleY;
                Apply();
            };
        }
        var slider = controls.ScaleSlider;
        slider.ValueChanged += (_, _) => { fit = false; scale = slider.Value / 100; Apply(); };
        controls.FitButton.Click += (_, _) => { fit = true; x = y = 0; Apply(); };
        foreach (var (button, percent) in new[] { (controls.Scale100Button, 100), (controls.Scale125Button, 125), (controls.Scale150Button, 150), (controls.Scale200Button, 200) })
            button.Click += (_, _) => { fit = false; slider.Value = Math.Round(percent / 10.0) * 10; scale = percent / 100.0; Apply(); };
        if (!externalControls) ZoomControls.Children.Add(controls);
        {
            WipeTab.Visibility = Visibility.Visible;
            Point? start = null;
            double initialX = 0, initialY = 0;
            WipeViewport.MouseLeftButtonDown += (_, e) =>
            {
                if (fit || e.Handled) return;
                start = e.GetPosition(WipeViewport);
                initialX = x;
                initialY = y;
                WipeViewport.CaptureMouse();
                e.Handled = true;
            };
            WipeViewport.MouseMove += (_, e) =>
            {
                if (start is not Point origin || !WipeViewport.IsMouseCaptured) return;
                var point = e.GetPosition(WipeViewport);
                var dpi = VisualTreeHelper.GetDpi(this);
                x = Math.Max(0, initialX - (point.X - origin.X) * dpi.DpiScaleX);
                y = Math.Max(0, initialY - (point.Y - origin.Y) * dpi.DpiScaleY);
                Apply();
                e.Handled = true;
            };
            WipeViewport.MouseLeftButtonUp += (_, e) =>
            {
                if (start is null) return;
                start = null;
                WipeViewport.ReleaseMouseCapture();
                e.Handled = true;
            };
            WipeViewport.LostMouseCapture += (_, _) => start = null;
        }
        HeatmapTab.Visibility = Visibility.Visible;
        Loaded += (_, _) => CompositionTarget.Rendering += Refresh;
        ZoomControls.Visibility = Visibility.Visible;
        void Refresh(object? sender, EventArgs args) => Apply();
        Unloaded += (_, _) => CompositionTarget.Rendering -= Refresh;
        Apply();
    }
}
