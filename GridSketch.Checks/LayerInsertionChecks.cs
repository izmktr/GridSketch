using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using GridSketch;

internal static class LayerInsertionChecks
{
    public static void Run(MainWindow window)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        var scene = new SceneDocument();
        var grid = SceneDocument.CreateGrid("空のグリッド"); scene.Canvas.Children.Add(grid);
        var first = new SceneObject { Kind = ObjectKind.Shape };
        var group = new SceneObject { Kind = ObjectKind.Group, Children = [new() { Kind = ObjectKind.Shape }] };
        grid.Children = [first, group];
        Set("document", scene);
        void Add(SceneObject selected, ObjectKind kind)
        {
            Set("selectedId", selected.Id); Call("RefreshAll");
            Call("AddClick", new MenuItem { Tag = kind.ToString() }, new RoutedEventArgs());
        }
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        Add(first, ObjectKind.Fill);
        Check(grid.Children[1].Kind == ObjectKind.Fill && grid.Children[2] == group, "Fill must be immediately below selected sibling");
        group.LayerExpanded = false; Add(group, ObjectKind.Text);
        Check(grid.Children[3].Kind == ObjectKind.Text && group.Children.Count == 1 && !group.LayerExpanded, "Closed group must receive a sibling");
        group.LayerExpanded = true; var oldChild = group.Children[0]; Add(group, ObjectKind.Shape);
        Check(group.Children.Count == 2 && group.Children[1] == oldChild, "Open group must receive first child");
        var empty = new SceneObject { Kind = ObjectKind.Group, LayerExpanded = false }; grid.Children.Add(empty);
        Add(empty, ObjectKind.Text);
        Check(empty.Children.Count == 1 && empty.LayerExpanded, "Empty group must receive a visible child regardless of stored expansion");
        grid.LayerExpanded = false; Add(grid, ObjectKind.Shape);
        Check(scene.Canvas.Children[1].Kind == ObjectKind.Shape && !grid.LayerExpanded, "Closed grid must receive a sibling");
        Set("selectedId", grid.Id);
        try { Call("AddPlacement", true); throw new Exception("Invalid grid-only sibling accepted"); }
        catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { }
        scene.Canvas.LayerExpanded = false; Add(scene.Canvas, ObjectKind.Group);
        Check(scene.Canvas.Children[0].Kind == ObjectKind.Group && scene.Canvas.LayerExpanded, "Root must always receive a child");
        Call("Undo");
        var restored = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        Check(restored.Canvas.Children[0].Id == grid.Id && !restored.Canvas.LayerExpanded, "Undo restores order and expansion");
        Set("savedState", restored.Snapshot());
        Console.WriteLine("PASS: Layer insertion order, open/closed/empty containers, root, grid-only restrictions and undo");
    }
}
