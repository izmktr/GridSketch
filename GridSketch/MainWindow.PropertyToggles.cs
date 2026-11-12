using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GridSketch;

public partial class MainWindow
{
    private void UpdatePropertyToggles()
    {
        var node = Selected;
        Properties.IsEnabled = document.Editable(node);
        bool available = node != document.Canvas;
        bool parentLocked = document.Parent(node) is { } parent && !document.Editable(parent);
        VisibilityToggle.Visibility = LockToggle.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        VisibilityToggle.IsEnabled = LockToggle.IsEnabled = !parentLocked;
        VisibilityToggle.IsChecked = node.Visible; LockToggle.IsChecked = node.Locked;
        VisibilityToggle.ToolTip = node.Visible ? "表示中（クリックで非表示）" : "非表示（クリックで表示）";
        LockToggle.ToolTip = parentLocked ? "親オブジェクトがロックされています" : node.Locked ? "ロック中（クリックで解除）" : "ロック解除中（クリックでロック）";
        FrameworkElement Icon(string data)
        {
            var icon = new Path { Data = Geometry.Parse(data), StrokeThickness = 1.6, Width = 20, Height = 20, Stretch = Stretch.Uniform };
            icon.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(System.Windows.Controls.Primitives.ToggleButton), 1)
            });
            return icon;
        }
        VisibilityToggle.Content = Icon("M1,10 Q10,-2 19,10 Q10,22 1,10 M13,10 A3,3 0 1 1 7,10 A3,3 0 1 1 13,10" + (node.Visible ? "" : " M1,1 L19,19"));
        LockToggle.Content = Icon("M4,9 L16,9 16,19 4,19 Z M10,13 L10,16 " + (node.Locked ? "M6,9 L6,5 A4,4 0 0 1 14,5 L14,9" : "M6,9 L6,5 A4,4 0 0 1 14,5"));
        PropertyLockNotice.Text = parentLocked ? "親オブジェクトがロックされています。" : node.Locked ? "ロック中です。解除してから内容を編集してください。" : "";
        PropertyLockNotice.Visibility = document.Editable(node) ? Visibility.Collapsed : Visibility.Visible;
    }
    private void VisibilityToggleClick(object sender, RoutedEventArgs e) => ToggleProperty(false);
    private void LockToggleClick(object sender, RoutedEventArgs e) => ToggleProperty(true);
    private void ToggleProperty(bool locking) => Try(() =>
    {
        CommitPropertyEdit();
        var node = Selected;
        if (node == document.Canvas || document.Parent(node) is { } parent && !document.Editable(parent)) { UpdatePropertyToggles(); return; }
        var before = document.Snapshot();
        if (locking) node.Locked = !node.Locked; else node.Visible = !node.Visible;
        RememberUndo(before);
        bool shown = document.Shown(node);
        if (!document.Editable(node) || !shown) GridInput.Visibility = Visibility.Collapsed;
        if (!shown) { Surface.ActiveCell = null; Surface.RangeEnd = null; }
        Surface.Preview = []; Surface.HoveredEdge = null;
        RefreshSurface(); UpdatePropertyToggles();
        // Update existing tree headers without replacing property editors or their pending values.
        void UpdateItems(ItemsControl tree)
        {
            foreach (var item in tree.Items.OfType<TreeViewItem>())
            {
                if (item.Tag is string id && document.Find(id) is { } layer) item.Header = layer.Label;
                UpdateItems(item);
            }
        }
        UpdateItems(Layers); UpdateTabHeaders();
        Title = $"{(Dirty ? "* " : "")}{(path is null ? "無題" : System.IO.Path.GetFileName(path))} — GridSketch";
        Status.Text = locking ? (node.Locked ? "ロックしました。" : "ロックを解除しました。") : (node.Visible ? "表示しました。" : "非表示にしました。");
        CommandManager.InvalidateRequerySuggested();
    });
}
