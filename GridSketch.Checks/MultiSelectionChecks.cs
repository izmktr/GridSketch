using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GridSketch;

internal static class MultiSelectionChecks
{
    public static void Run(MainWindow window)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        var doc = new SceneDocument(); var grid = SceneDocument.CreateGrid("基本の方眼紙"); doc.Canvas.Children.Add(grid);
        Set("document", doc); Set("selectedId", grid.Id); Call("RefreshAll");
        var layers = (TreeView)window.FindName("Layers");
        TreeViewItem Item(int index) => (TreeViewItem)((TreeViewItem)((TreeViewItem)layers.Items[0]).Items[0]).Items[index];
        var originals = grid.Children.ToList();
        Call("ChooseLayer", Item(0), false, false, false);
        Call("ChooseLayer", Item(2), true, false, false);
        Check(Call("GroupSelectionError") is string, "Nonadjacent grouping rejected");
        Call("ChooseLayer", Item(0), false, false, false);
        Call("ChooseLayer", Item(2), false, true, false);
        Check(Call("GroupSelectionError") is null, "Shift selects contiguous siblings");
        Item(1).RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.MouseRightButtonDownEvent });
        Check(((List<SceneObject>)Call("SelectedLayers")!).Count == 3, "Right click preserves selected range");
        var menu = new ContextMenu(); Call("PopulateLayerMenu", menu);
        Check(!menu.Items.OfType<MenuItem>().Single(m => Equals(m.Header, "削除")).IsEnabled, "Unsupported bulk deletion disabled");
        Call("WrapClick", window, new RoutedEventArgs());
        var group = grid.Children[0];
        Check(group.Kind == ObjectKind.Group && group.OrganizeOnly && group.Children.SequenceEqual(originals.Take(3)), "Bulk grouping preserves order and creates organizer");
        Call("Undo"); doc = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        Check(doc.Find(grid.Id)!.Children.Select(c => c.Id).SequenceEqual(originals.Select(c => c.Id)), "Bulk grouping undoes in one step");
        Call("ChooseLayer", Item(0), false, false, false);
        Call("ChooseLayer", Item(1), true, false, false);
        Call("ChooseLayer", Item(1), true, false, false);
        Check(((List<SceneObject>)Call("SelectedLayers")!).Count == 1, "Ctrl toggles individual selection");
        Set("savedState", doc.Snapshot());
        Console.WriteLine("PASS: Multi-layer Ctrl/Shift selection, right-click retention, grouping order, restrictions and undo");
    }
}
