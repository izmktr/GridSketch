using System.Windows;
using GridSketch;

internal static class EdgeSelectionChecks
{
    public static void Run(MainWindow window)
    {
        var doc = new SceneDocument();
        var grid = SceneDocument.CreateGrid("空のグリッド", 3, 2, 40); doc.Canvas.Children.Add(grid);
        var selector = new SceneObject { Kind = ObjectKind.EdgeSelection, UseObjectLayout = true }; grid.Children.Add(selector);
        var shape = new SceneObject { Kind = ObjectKind.Shape, Width = 30, Height = 10, X = 5, Y = 15, Thickness = 0 };
        selector.Children.Add(shape); SceneDocument.AssignNewState(selector, shape);
        var edge = new GridEdge(0, 0, false);
        var stroke = new GridPaintStroke(doc, selector, grid, new Point(20, 0), edge);
        stroke.Move(new Point(100, 0));
        if (selector.StateEdges.Count != 3) throw new Exception("Edge drag did not fill contiguous edges");
        new GridPaintStroke(doc, selector, grid, new Point(20, 0), edge);
        if (selector.StateEdges.Any(e => e.Edge == edge)) throw new Exception("Edge cycle did not clear");
        selector.StateEdges.Clear();
        var vertical = new GridEdge(0, 0, true);
        SceneDocument.SetEdgeState(selector, vertical, selector.States[0].Id);
        doc.Validate();
        var rect = new SceneRenderer(doc).Bounds[selector.Id];
        if (Math.Abs(rect.Width - 10) > 0.01 || Math.Abs(rect.Height - 30) > 0.01 || Math.Abs(rect.X + 5) > 0.01) throw new Exception("Vertical template rotation/centering failed");
        shape.VerticalRotation = "回転なし";
        rect = new SceneRenderer(doc).Bounds[selector.Id];
        if (Math.Abs(rect.Width - 30) > 0.01 || Math.Abs(rect.Height - 10) > 0.01) throw new Exception("No rotation failed");
        selector.EdgeFilter = "水平辺のみ";
        if (SceneDocument.EdgeSelectable(selector, vertical) || !SceneDocument.EdgeSelectable(selector, edge)) throw new Exception("Edge filter failed");
        var copy = SceneDocument.Restore(doc.Snapshot()); copy.Validate();
        if (copy.Snapshot() != doc.Snapshot()) throw new Exception("Edge state persistence failed");
        var duplicate = doc.DuplicateInto([selector], grid).Single();
        if (duplicate.StateEdges.Single().StateId != duplicate.States.Single().Id || duplicate.States.Single().ObjectId != duplicate.Children.Single().Id) throw new Exception("Edge duplicate references failed");
        var source = SceneDocument.Restore(doc.Snapshot()).Find(selector.Id)!;
        doc.MoveCellContents(selector, source, grid, new Vector(40, 40)); doc.Validate();
        if (selector.StateEdges.Single().Edge != new GridEdge(1, 1, true)) throw new Exception("Edge movement failed");
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        void Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        Set("document", doc); Set("selectedId", selector.Id); Call("RefreshAll");
        var panel = (System.Windows.Controls.StackPanel)window.FindName("Properties");
        var toggle = PropertyChecks.Find<System.Windows.Controls.Primitives.ToggleButton>(panel, "編集対象：垂直辺のみ");
        toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        if (selector.EdgeFilter != "垂直辺のみ") throw new Exception("Edge filter UI did not apply immediately");
        Call("Undo");
        var restored = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        if (restored.Find(selector.Id)!.EdgeFilter != "水平辺のみ") throw new Exception("Edge filter undo failed");
        var rotation = PropertyChecks.Find<System.Windows.Controls.Button>(panel, "状態1の垂直辺での向き");
        foreach (var expected in new[] { "右90°", "左90°", "回転なし" })
        {
            rotation.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            if (restored.Find(shape.Id)!.VerticalRotation != expected) throw new Exception("State row rotation cycle failed");
        }
        rotation.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Call("Undo");
        restored = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        if (restored.Find(shape.Id)!.VerticalRotation != "回転なし") throw new Exception("State row rotation undo failed");
        try { doc.ResizeGrid(grid, 1, 1, 40, 5); throw new Exception("Resize discarded edge"); } catch (InvalidOperationException) { }
    }
}
