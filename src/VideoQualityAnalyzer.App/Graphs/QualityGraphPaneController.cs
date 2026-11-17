using System.Windows;
using System.Windows.Controls;

namespace VideoQualityAnalyzer.App;

internal sealed class QualityGraphPaneController
{
    private readonly Grid grid;
    private readonly DockPanel leftGraph;
    private readonly DockPanel rightGraph;
    private readonly GridSplitter splitter;
    private readonly bool frameOnly;
    private readonly Dictionary<int, Button> toggles = new();

    public int MinimizedColumn { get; private set; } = -1;
    public GridLength SavedLeft { get; private set; } = new(1, GridUnitType.Star);
    public GridLength SavedRight { get; private set; } = new(1, GridUnitType.Star);
    public Button FrameButton { get; }
    public Button FileButton { get; }

    public QualityGraphPaneController(Grid grid, DockPanel leftGraph, DockPanel rightGraph,
        GridSplitter splitter, bool frameOnly)
    {
        this.grid = grid;
        this.leftGraph = leftGraph;
        this.rightGraph = rightGraph;
        this.splitter = splitter;
        this.frameOnly = frameOnly;
        FrameButton = AddMinimize(leftGraph, 0, "フレームグラフ");
        FileButton = AddMinimize(rightGraph, 2, "ファイルグラフ");
        if (frameOnly)
        {
            splitter.Visibility = Visibility.Collapsed;
            grid.ColumnDefinitions[1].Width = new GridLength(0);
        }
    }

    private Button AddMinimize(DockPanel graph, int column, string name)
    {
        var title = graph.Children[0];
        var header = new DockPanel();
        var toggle = new Button { Content = column == 0 ? "◀" : "▶", MinWidth = 24,
            Padding = new Thickness(2), ToolTip = name + "を最小化" };
        toggles[column] = toggle;
        graph.Children.Remove(title);
        DockPanel.SetDock(toggle, column == 0 ? Dock.Right : Dock.Left);
        header.Children.Add(toggle);
        header.Children.Add(title);
        DockPanel.SetDock(header, Dock.Top);
        graph.Children.Insert(0, header);
        toggle.Click += (_, _) =>
        {
            var restore = MinimizedColumn != -1;
            if (MinimizedColumn == -1)
            {
                SavedLeft = grid.ColumnDefinitions[0].Width;
                SavedRight = grid.ColumnDefinitions[2].Width;
            }
            MinimizedColumn = restore ? -1 : column;
            foreach (var (pane, paneColumn) in new[] { (leftGraph, 0), (rightGraph, 2) })
            {
                var minimized = MinimizedColumn == paneColumn;
                var paneHeader = (DockPanel)pane.Children[0];
                paneHeader.Children[1].Visibility = minimized ? Visibility.Collapsed : Visibility.Visible;
                foreach (UIElement child in pane.Children)
                    if (child != paneHeader) child.Visibility = minimized ? Visibility.Collapsed : Visibility.Visible;
                grid.ColumnDefinitions[paneColumn].Width = minimized ? new GridLength(0) :
                    MinimizedColumn == -1 ? (paneColumn == 0 ? SavedLeft : SavedRight) : new GridLength(1, GridUnitType.Star);
                toggles[paneColumn].Visibility = minimized ? Visibility.Collapsed : Visibility.Visible;
                toggles[paneColumn].Content = paneColumn == 0 ? "◀" : "▶";
                toggles[paneColumn].ToolTip = MinimizedColumn == -1
                    ? (paneColumn == 0 ? "フレームグラフ" : "ファイルグラフ") + "を最小化"
                    : (MinimizedColumn == 0 ? "フレームグラフ" : "ファイルグラフ") + "を復元";
            }
            splitter.IsEnabled = MinimizedColumn == -1;
            splitter.Visibility = MinimizedColumn == -1 && !frameOnly ? Visibility.Visible : Visibility.Collapsed;
            grid.ColumnDefinitions[1].Width = new GridLength(MinimizedColumn == -1 && !frameOnly ? 10 : 0);
        };
        return toggle;
    }
}
