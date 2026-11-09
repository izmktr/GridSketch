using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace GridSketch;

public partial class MainWindow
{
    private readonly HashSet<string> layerSelection = [];
    private string? layerAnchor;
    private bool selectingLayers;
    private List<SceneObject> SelectedLayers() => layerSelection.Contains(Selected.Id)
        ? document.All().Where(n => layerSelection.Contains(n.Id)).ToList() : [Selected];
    private void SyncLayerSelection()
    {
        layerSelection.RemoveWhere(id => document.Find(id) is null);
        if (!layerSelection.Contains(Selected.Id)) { layerSelection.Clear(); layerSelection.Add(Selected.Id); }
    }
    private IEnumerable<TreeViewItem> LayerItems(ItemsControl owner)
    {
        foreach (var item in owner.Items.OfType<TreeViewItem>())
        {
            yield return item;
            foreach (var child in LayerItems(item)) yield return child;
        }
    }
    private void PaintLayerSelection()
    {
        foreach (var item in LayerItems(Layers))
            item.Background = item.Tag is string id && layerSelection.Contains(id) ? new SolidColorBrush(Color.FromRgb(202, 224, 250)) : Brushes.Transparent;
    }
    private void ChooseLayer(TreeViewItem item, bool control, bool shift, bool right = false)
    {
        var id = (string)item.Tag;
        if (id != selectedId) { CommitPropertyEdit(); BreakUndoMerge(); }
        SyncLayerSelection();
        if (right && layerSelection.Contains(id)) { control = shift = false; }
        else if (shift && layerAnchor is not null)
        {
            var visible = new List<string>();
            void Walk(SceneObject n) { visible.Add(n.Id); if (n.LayerExpanded) foreach (var c in n.Children) Walk(c); }
            Walk(document.Canvas);
            int a = visible.IndexOf(layerAnchor), b = visible.IndexOf(id);
            if (!control) layerSelection.Clear();
            if (a >= 0 && b >= 0) foreach (var value in visible.Skip(Math.Min(a, b)).Take(Math.Abs(a - b) + 1)) layerSelection.Add(value);
            else layerSelection.Add(id);
        }
        else if (control)
        {
            if (!layerSelection.Remove(id)) layerSelection.Add(id);
            if (layerSelection.Count == 0) layerSelection.Add(id);
            layerAnchor = id;
        }
        else { layerSelection.Clear(); layerSelection.Add(id); layerAnchor = id; }
        selectedId = layerSelection.Contains(id) ? id : layerSelection.First();
        selectingLayers = true;
        try { item.Focus(); foreach (var entry in LayerItems(Layers)) entry.IsSelected = Equals(entry.Tag, selectedId); }
        finally { selectingLayers = false; }
        Surface.Selected = Selected; Surface.ActiveCell = null; Surface.RangeEnd = null; Surface.Preview = []; Surface.InvalidateVisual();
        PaintLayerSelection(); BuildProperties();
    }
    private void LayerMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        var current = e.OriginalSource as DependencyObject;
        while (current is not null && current is not TreeViewItem)
        {
            if (current is ToggleButton) return; // Keep expand/collapse independent of selection.
            current = VisualTreeHelper.GetParent(current);
        }
        if (current is not TreeViewItem item) return;
        ChooseLayer(item, Keyboard.Modifiers.HasFlag(ModifierKeys.Control), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        e.Handled = true;
    }
    private string? GroupSelectionError()
    {
        var nodes = SelectedLayers();
        if (nodes.Any(n => n == document.Canvas)) return "キャンバスはグループ化できません。";
        if (nodes.Any(n => !document.Editable(n))) return "ロックされたレイヤーが含まれています。";
        var parent = document.Parent(nodes[0])!;
        if (nodes.Any(n => document.Parent(n) != parent)) return "同じ親の下にあるレイヤーを選択してください。";
        if (nodes.Count > 1 && parent.IsStateSelector) return "切り替えオブジェクトの複数の見本はまとめられません。";
        var indices = nodes.Select(n => parent.Children.IndexOf(n)).Order().ToArray();
        if (indices[^1] - indices[0] + 1 != nodes.Count) return "描画順を維持するため、間に未選択レイヤーを含まない範囲を選択してください。";
        return null;
    }
    private void RequireSingleLayer()
    {
        if (SelectedLayers().Count > 1) throw new InvalidOperationException("この操作はレイヤーを1つ選択して実行してください。");
    }
}
