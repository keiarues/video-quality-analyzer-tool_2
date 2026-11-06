using System.Windows.Controls;
namespace VideoQualityAnalyzer.GpuPreview;
public partial class ZoomControlBar : UserControl
{
    public bool ShowSlider { get => SliderGroup.Visibility == System.Windows.Visibility.Visible; set => SliderGroup.Visibility = value ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed; }
    public bool ShowButtons { get => ButtonGroup.Visibility == System.Windows.Visibility.Visible; set => ButtonGroup.Visibility = value ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed; }
    public ZoomControlBar() => InitializeComponent();
}
