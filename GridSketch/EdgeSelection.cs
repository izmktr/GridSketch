using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;

namespace GridSketch;

public sealed class EdgeStatePlacement
{
    public GridEdge Edge { get; set; }
    public string StateId { get; set; } = "";
}

public sealed partial class SceneDocument
{
    public static bool EdgeSelectable(SceneObject node, GridEdge edge) => node.EdgeFilter == "すべて" || (edge.Vertical ? node.EdgeFilter == "垂直辺のみ" : node.EdgeFilter == "水平辺のみ");
    public static string? NextEdgeState(SceneObject node, GridEdge edge)
    {
        var current = node.StateEdges.Find(p => p.Edge == edge)?.StateId;
        int index = current is null ? -1 : node.States.FindIndex(s => s.Id == current);
        return index + 1 < node.States.Count ? node.States[index + 1].Id : null;
    }
    public static void SetEdgeState(SceneObject node, GridEdge edge, string? state)
    {
        node.StateEdges.RemoveAll(p => p.Edge == edge);
        if (state is not null) node.StateEdges.Add(new() { Edge = edge, StateId = state });
    }
    public static Point EdgeCenter(SceneObject grid, GridEdge edge) => new((edge.Column + (edge.Vertical ? 0 : 0.5)) * grid.CellSize, (edge.Row + (edge.Vertical ? 0.5 : 0)) * grid.CellSize);
}

public partial class MainWindow
{
    private static FrameworkElement EdgeRotationIcon(string rotation)
    {
        var icon = new Grid { Width = 28, Height = 28, IsHitTestVisible = false };
        if (rotation != "回転なし")
        {
            // Quarter-circle from twelve o'clock to three (or nine) o'clock.
            var arrow = new System.Windows.Shapes.Path
            {
                Data = System.Windows.Media.Geometry.Parse(rotation == "右90°"
                    ? "M14,2 A12,12 0 0 1 26,14 M22,10 L26,14 28,9"
                    : "M14,2 A12,12 0 0 0 2,14 M0,9 L2,14 6,10"),
                StrokeThickness = 1.5, StrokeLineJoin = System.Windows.Media.PenLineJoin.Round
            };
            arrow.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground")
            { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1) });
            icon.Children.Add(arrow);
        }
        icon.Children.Add(new TextBlock { Text = rotation == "回転なし" ? "0°" : "90°", FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        return icon;
    }

    private void SelectorIcons(string label, string selected, (string Value, string Icon)[] choices, Action<string> setter)
    {
        Label(label);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 8) };
        Properties.Children.Add(row);
        foreach (var (value, icon) in choices)
        {
            var button = new ToggleButton { Content = icon, ToolTip = value, Tag = value, Width = 48, Height = 32, FontSize = 22,
                Margin = new Thickness(2), IsChecked = selected == value, Style = (Style)FindResource("IconToggleStyle") };
            AutomationProperties.SetName(button, label + "：" + value);
            row.Children.Add(button);
            button.Click += (_, _) => { ImmediateProperty(button, () => setter(value)); foreach (var sibling in row.Children.OfType<ToggleButton>()) sibling.IsChecked = Equals(sibling.Tag, value); };
        }
    }
    private void EdgeSelectionProperties(SceneObject node)
    {
        SelectorIcons("編集対象", node.EdgeFilter, [("すべて", "田"), ("水平辺のみ", "☰"), ("垂直辺のみ", "Ⅲ")], v => node.EdgeFilter = v);
        CellSelectionProperties(node);
        Button("すべての辺を空にする", () => Change(() => { RequireEditable(node); node.StateEdges.Clear(); }));
    }
}
