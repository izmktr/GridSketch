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
        var fixedGroup = new SceneObject { Kind = ObjectKind.Group, HasLayoutFrame = true, Width = 120, Height = 80,
            LayoutHasInner = true, LayoutInsetX = 10, LayoutInsetY = 15 };
        scene.Canvas.Children.Add(fixedGroup);
        var organizer = new SceneObject { Kind = ObjectKind.Group, OrganizeOnly = true }; fixedGroup.Children.Add(organizer);
        if (scene.InitialSquareSize(scene.Canvas) != 100 || scene.InitialSquareSize(fixedGroup) != 50 || scene.InitialSquareSize(organizer) != 50)
            throw new Exception("Initial square size must use parent inner space, organizer pass-through, or fallback");
        scene.Canvas.AutoSize = false; scene.Canvas.Width = 640; scene.Canvas.Height = 480;
        if (scene.InitialSquareSize(scene.Canvas) != 480) throw new Exception("Fixed canvas initial size failed");
        scene.Canvas.AutoSize = true; scene.Canvas.Children.Clear();
        var grid = SceneDocument.CreateGrid("空のグリッド"); scene.Canvas.Children.Add(grid);
        var first = new SceneObject { Kind = ObjectKind.Shape };
        var group = new SceneObject { Kind = ObjectKind.Group, Children = [new() { Kind = ObjectKind.Shape }] };
        grid.Children = [first, group];
        Set("document", scene);
        foreach (var kind in new[] { ObjectKind.Image, ObjectKind.Shape })
        {
            var node = new SceneObject { Kind = kind };
            Call("InitializeObjectLayout", node, grid);
            double side = Math.Min(grid.CellSize - grid.Margin * 2, grid.CellSize - grid.MarginY * 2);
            if (node.Width != side || node.Height != side) throw new Exception("New image/shape must use one inset cell, not the full grid");
            double initial = node.Width; grid.CellSize += 10;
            if (node.Width != initial) throw new Exception("Initial size must not track later parent edits");
            grid.CellSize -= 10;
        }
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
        Check(group.Children.Count == 2 && group.Children[0] == oldChild && group.Children[1].Kind == ObjectKind.Shape, "Open group must append the new child below existing children");
        var empty = new SceneObject { Kind = ObjectKind.Group, LayerExpanded = false }; grid.Children.Add(empty);
        Add(empty, ObjectKind.Text);
        Check(empty.Children.Count == 1 && empty.LayerExpanded, "Empty group must receive a visible child regardless of stored expansion");
        grid.LayerExpanded = false; Add(grid, ObjectKind.Shape);
        Check(scene.Canvas.Children[1].Kind == ObjectKind.Shape && !grid.LayerExpanded, "Closed grid must receive a sibling");
        Set("selectedId", grid.Id);
        try { Call("AddPlacement", true); throw new Exception("Invalid grid-only sibling accepted"); }
        catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { }
        scene.Canvas.LayerExpanded = false; Add(scene.Canvas, ObjectKind.Group);
        Check(scene.Canvas.Children[^1].Kind == ObjectKind.Group && scene.Canvas.Children[0] == grid && scene.Canvas.LayerExpanded, "Root must append the new child below existing children");
        Call("Undo");
        var restored = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        Check(restored.Canvas.Children[0].Id == grid.Id && !restored.Canvas.LayerExpanded, "Undo restores order and expansion");
        Set("savedState", restored.Snapshot());
        Console.WriteLine("PASS: Layer insertion order, open/closed/empty containers, root, grid-only restrictions and undo");
    }
}
