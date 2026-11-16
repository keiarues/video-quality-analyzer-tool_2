using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public sealed class EvaluationTargetListControl : UserControl
{
    private readonly DataGrid list = new()
    {
        Height = 150,
        SelectionMode = DataGridSelectionMode.Extended,
        SelectionUnit = DataGridSelectionUnit.FullRow,
        AutoGenerateColumns = false,
        IsReadOnly = true,
        CanUserAddRows = false,
        CanUserDeleteRows = false,
        RowHeaderWidth = 0,
        Background = new SolidColorBrush(Color.FromRgb(23, 32, 45)),
        RowBackground = new SolidColorBrush(Color.FromRgb(23, 32, 45)),
        AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(32, 40, 55)),
        BorderThickness = new Thickness(0),
        HeadersVisibility = DataGridHeadersVisibility.Column,
        Margin = new Thickness(0, 6, 0, 8)
    };
    private VideoEntry[] entries = [];
    public event EventHandler? SelectionChanged;
    public VideoDescriptor[] SelectedVideos => entries.Where(list.SelectedItems.Contains).Select(entry => entry.Descriptor).ToArray();
    internal DataGrid List => list;

    public EvaluationTargetListControl()
    {
        Style = (Style)Application.Current.FindResource(typeof(UserControl));
        list.Foreground = (Brush)FindResource("TextBrush");
        var header = new Style(typeof(DataGridColumnHeader));
        header.Setters.Add(new Setter(Control.ForegroundProperty, FindResource("UiOnLightBrush")));
        header.Setters.Add(new Setter(DataGridColumnHeader.PaddingProperty, new Thickness(5, 6, 5, 6)));
        list.ColumnHeaderStyle = header;
        AddColumn("ファイル名", "DisplayName", 230);
        AddColumn("グループ", "Group", 100);
        AddColumn("コーデック", "Descriptor.Codec", 80);
        AddColumn("kbps", "AverageBitrateKbps", 75, "N0");
        AddColumn("VMAF", "VmafAverage", 75, "F4");
        AddColumn("SSIM", "SsimAverage", 75, "F4");
        AddColumn("PSNR", "PsnrAverage", 75, "F4");
        AddColumn("解像度", "Resolution", 95);
        AddColumn("fps", "FrameRateText", 95);
        AutomationProperties.SetName(list, "評価対象動画");
        Content = list;
        list.SelectionChanged += (_, _) => SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AddColumn(string header, string path, double width, string? format = null)
    {
        var binding = new Binding(path);
        if (format is not null) binding.StringFormat = format;
        list.Columns.Add(new DataGridTextColumn { Header = header, Binding = binding, Width = width });
    }

    public void SetEntries(IEnumerable<VideoEntry> videos, IEnumerable<string>? selectedIds = null)
    {
        entries = videos.Where(entry => !entry.IsReference).ToArray();
        list.ItemsSource = entries;
        SelectIds(selectedIds ?? entries.Select(entry => entry.Descriptor.Id));
    }

    public void SelectIds(IEnumerable<string> selectedIds)
    {
        list.UnselectAll();
        var selected = selectedIds.ToHashSet();
        foreach (var entry in entries.Where(entry => selected.Contains(entry.Descriptor.Id)))
            list.SelectedItems.Add(entry);
    }
}
