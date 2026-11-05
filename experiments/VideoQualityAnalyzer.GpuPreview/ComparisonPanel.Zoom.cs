using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VideoQualityAnalyzer.GpuPreview;

public partial class ComparisonPanel
{
    // 専用比較ウィンドウだけで有効にする。再生とフレーム選択は既存の共通処理を使う。
    public void EnableZoomTest()
    {
        var images = new[] { LeftImage, RightImage, HeatmapImage };
        var viewers = new List<ScrollViewer>();
        var fit = true;
        var updating = false;
        var scale = 1.0;
        var x = 0.0;
        var y = 0.0;
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 54, Margin = new Thickness(6, 0, 0, 0) };
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
        var slider = new Slider { Minimum = 10, Maximum = 500, TickFrequency = 10, IsSnapToTickEnabled = true, Value = 100, Width = 260, VerticalAlignment = VerticalAlignment.Center };
        slider.ValueChanged += (_, _) => { fit = false; scale = slider.Value / 100; Apply(); };
        controls.Children.Add(slider);
        controls.Children.Add(label);
        void AddButton(string text, Action action, string? toolTip = null)
        {
            var button = new Button { Content = text, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(4, 4, 4, 4), MinWidth = 0, ToolTip = toolTip ?? text };
            button.Click += (_, _) => action();
            controls.Children.Add(button);
        }
        AddButton("⛶", () => { fit = true; x = y = 0; Apply(); }, "画面に合わせる");
        foreach (var percent in new[] { 100, 125, 150, 200 })
            AddButton($"{percent}%", () => { fit = false; slider.Value = Math.Round(percent / 10.0) * 10; scale = percent / 100.0; Apply(); });

        CommonControls.Children.Add(controls);
        WipeTab.Visibility = Visibility.Collapsed;
        HeatmapTab.Visibility = Visibility.Visible;
        CompositionTarget.Rendering += Refresh;
        void Refresh(object? sender, EventArgs args) => Apply();
        Unloaded += (_, _) => CompositionTarget.Rendering -= Refresh;
        Apply();
    }
}
