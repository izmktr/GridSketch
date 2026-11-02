using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GridSketch;

public partial class PropertyHelpWindow : Window
{
    private readonly object cellSelectionContent;
    private string? currentTopic;
    public PropertyHelpWindow()
    {
        InitializeComponent();
        cellSelectionContent = HelpScroll.Content;
    }

    public static string TopicName(SceneObject node) => node.Kind == ObjectKind.Border && node.ConnectCellCenters
        ? "中心罫線" : SceneObject.KindName(node.Kind);

    public void ShowTopic(SceneObject node)
    {
        string key = node.Kind + (node.Kind == ObjectKind.Border && node.ConnectCellCenters ? ":center" : "");
        if (key == currentTopic) return;
        currentTopic = key;
        Title = HelpHeading.Text = TopicName(node) + "のヘルプ";
        var (summary, sections) = ObjectHelpContent.For(node);
        HelpSubtitle.Text = summary;
        if (node.Kind == ObjectKind.CellSelection) HelpScroll.Content = cellSelectionContent;
        else
        {
            var panel = new StackPanel();
            foreach (var (heading, body) in sections)
            {
                panel.Children.Add(new Expander
                {
                    Header = heading, IsExpanded = panel.Children.Count == 0, Margin = new Thickness(0, 0, 0, 14),
                    Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap, LineHeight = 22, Margin = new Thickness(0, 8, 0, 0) }
                });
            }
            panel.Children.Add(new TextBlock { Text = "変更は即時反映されます。数値・文字は項目の編集終了、スライダーは手を離した時にUndoをまとめます。Ctrl＋Zで戻せます。",
                TextWrapping = TextWrapping.Wrap, LineHeight = 22, Foreground = new SolidColorBrush(Color.FromRgb(82, 103, 125)), Margin = new Thickness(0, 4, 0, 12) });
            HelpScroll.Content = panel;
        }
        HelpScroll.ScrollToTop();
    }
}
