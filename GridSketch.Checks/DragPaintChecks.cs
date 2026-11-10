using System.Reflection;
using System.Windows;
using GridSketch;

internal static class DragPaintChecks
{
    public static void Run(MainWindow window)
    {
        var scene = new SceneDocument(); var grid = SceneDocument.CreateGrid("独立マス");
        grid.Columns = 8; grid.Rows = 6; scene.Canvas.Children.Add(grid);
        Point P(double x, double y) => new(x * grid.CellSize, y * grid.CellSize);
        var border = new SceneObject { Kind = ObjectKind.Border, Pattern = "個別" }; grid.Children.Add(border);
        var stroke = new GridPaintStroke(scene, border, grid, P(0.5, 1), new GridEdge(0, 1, false));
        stroke.Move(P(5.7, 1.12));
        if (border.Edges.Count != 6 || border.Edges.Any(e => e.Vertical)) throw new Exception("Horizontal stroke has gaps or unwanted vertical edges");
        stroke.Move(P(6, 1.18));
        if (border.Edges.Any(e => e.Vertical)) throw new Exception("Junction jitter created a vertical edge");
        stroke.Move(P(6, 4.7));
        if (border.Edges.Count != 10 || !Enumerable.Range(1, 4).All(r => border.Edges.Contains(new(6, r, true)))) throw new Exception("Corner or fast vertical stroke failed");
        stroke.Move(P(6, 1)); stroke.Move(P(0.5, 1));
        if (border.Edges.Count != 10) throw new Exception("Retracing toggled edges");
        var erase = new GridPaintStroke(scene, border, grid, P(0.5, 1), new GridEdge(0, 1, false));
        erase.Move(P(7.8, 1)); erase.Move(P(0.5, 1));
        if (border.Edges.Count != 4 || border.Edges.Any(e => !e.Vertical)) throw new Exception("Erase mode changed at an empty edge");
        var fill = grid.Children.Single(n => n.Kind == ObjectKind.Fill);
        fill.FilledCells.Add(new(2, 2));
        var paint = new GridPaintStroke(scene, fill, grid, P(0.5, 2.5));
        paint.Move(P(7.5, 2.5)); paint.Move(P(0.5, 2.5));
        if (fill.FilledCells.Count != 8) throw new Exception("Fill interpolation/retracing failed");
        paint = new GridPaintStroke(scene, fill, grid, P(0.5, 2.5)); paint.Move(P(7.5, 2.5));
        if (fill.FilledCells.Count != 0) throw new Exception("Fill erase failed");
        var frame = grid.Children.Single(n => n.Kind == ObjectKind.CellFrame);
        paint = new GridPaintStroke(scene, frame, grid, P(0.5, 0.5)); paint.Move(P(7.5, 0.5));
        if (frame.FrameCells.Count != 8 || frame.RegionLinks.Count != 7) throw new Exception("Region drag did not join successive cells");
        frame.FrameAllCells = true; frame.FrameCells.Clear(); frame.RegionLinks.Clear();
        paint = new GridPaintStroke(scene, frame, grid, P(0.5, 0.5)); paint.Move(P(7.5, 0.5));
        if (frame.FrameAllCells || frame.FrameCells.Count != 40) throw new Exception("Frame preset erase failed");
        paint.Move(P(-2, 0.5)); paint.Move(P(7.5, 5.5));
        if (frame.FrameCells.Count != 39) throw new Exception("Reentry painted an unintended connecting path");
        scene.Validate();

        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        void Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        grid.Angle = 25; grid.X = -30;
        Set("document", scene); Set("selectedId", fill.Id); Call("RefreshAll");
        var surface = (SceneSurface)window.FindName("Surface");
        var world = scene.World(grid).Transform(P(0.5, 0.5));
        var point = new Point(world.X + surface.OriginX, world.Y + surface.OriginY);
        var before = scene.Snapshot();
        // Supply the gesture state without capturing the real mouse (no physical button is held in this test).
        var active = new GridPaintStroke(scene, fill, grid, surface.ToLocal(grid, point));
        Set("dragSnapshot", before); Set("paintStroke", active); Set("paintGrid", grid);
        active.Move(P(7.5, 0.5)); Call("FinishDrag"); surface.ReleaseMouseCapture();
        if (fill.FilledCells.Count != 8) throw new Exception("Transformed grid paint failed");
        Call("Undo");
        var restored = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        if (restored.Snapshot() != before) throw new Exception("Stroke did not undo atomically");
        Call("Redo");
        restored = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        if (restored.Find(fill.Id)!.FilledCells.Count != 8) throw new Exception("Stroke redo failed");
        Set("savedState", restored.Snapshot());
        Console.WriteLine("PASS: Continuous border turns, jitter rejection, interpolation, retracing, fill/frame erase, reentry, rotated grid and atomic undo/redo");
    }
}
