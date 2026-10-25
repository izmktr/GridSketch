using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using GridSketch;

internal static class CenterBorderChecks
{
    public static void Run(MainWindow window, string directory)
    {
        var scene = new SceneDocument(); var grid = SceneDocument.CreateGrid("空のグリッド", 4, 3, 40); scene.Canvas.Children.Add(grid);
        var border = new SceneObject { Kind = ObjectKind.Border, ConnectCellCenters = true, Thickness = 8, Name = "中心罫線" }; grid.Children.Add(border);
        var stroke = new GridPaintStroke(scene, border, grid, new Point(40, 20), new GridEdge(0, 0, false));
        stroke.Move(new Point(140, 20)); stroke.Move(new Point(140, 100));
        if (border.Edges.Count != 5 || border.Edges.Any(e => !SceneDocument.BorderEdgeInside(border, grid, e))) throw new Exception("Center border drag failed");
        scene.Validate(); var renderer = new SceneRenderer(scene);
        if (renderer.Bounds[border.Id] != new Rect(16, 16, 128, 88)) throw new Exception("Center border draw offset failed");
        var surface = new SceneSurface { Selected = border }; surface.Refresh(scene);
        Point At(double x, double y) => new(surface.OriginX + x, surface.OriginY + y);
        if (surface.EdgeAt(grid, At(40, 20), 1) != new GridEdge(0, 0, false) || surface.EdgeAt(grid, At(40, 0), 1) is not null) throw new Exception("Center hit testing used grid boundaries");
        grid.Angle = 30; grid.X = 50; surface.Refresh(scene);
        var world = scene.World(grid).Transform(new Point(40, 20));
        if (surface.EdgeAt(grid, At(world.X, world.Y), 2) != new GridEdge(0, 0, false)) throw new Exception("Rotated center hit test failed");
        grid.Angle = grid.X = 0;
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(new SceneRenderer(scene).Export()));
        using (var file = File.Create(Path.Combine(directory, "center-border.png"))) encoder.Save(file);
        PdfExporter.Save(scene, Path.Combine(directory, "center-border.pdf"));
        scene.Save(Path.Combine(directory, "center-border.gridsketch"));
        if (!SceneDocument.Load(Path.Combine(directory, "center-border.gridsketch")).Canvas.Children[0].Children[0].ConnectCellCenters) throw new Exception("Center border flag lost");
        scene.ResizeGrid(grid, 2, 2, 40, 5);
        if (!border.Edges.SetEquals([new GridEdge(0, 0, false)])) throw new Exception("Center resize did not crop outside links");
        border.Pattern = "すべて"; border.Edges.Clear();
        scene.ResizeGrid(grid, 1, 3, 40, 5);
        if (SceneDocument.BorderEdges(border, grid).Count() != 2) throw new Exception("One-column center links failed");
        surface.Refresh(scene);
        if (surface.EdgeAt(grid, At(20, 40), 1) != new GridEdge(0, 0, true)) throw new Exception("One-column hit test failed");
        scene.ResizeGrid(grid, 3, 1, 40, 5); surface.Refresh(scene);
        if (surface.EdgeAt(grid, At(40, 20), 1) != new GridEdge(0, 0, false)) throw new Exception("One-row hit test failed");
        scene.ResizeGrid(grid, 1, 1, 40, 5); surface.Refresh(scene);
        if (SceneDocument.BorderEdges(border, grid).Any() || surface.EdgeAt(grid, At(20, 20), 1) is not null) throw new Exception("Single cell has invalid links");
        scene.ResizeGrid(grid, 3, 3, 40, 5); border.Pattern = "個別";
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        void Call(string name) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, null);
        Set("document", scene); Set("selectedId", border.Id); Call("RefreshAll");
        var live = (SceneSurface)window.FindName("Surface"); live.ActiveCell = new(0, 0); live.RangeEnd = new(1, 1);
        var panel = (StackPanel)window.FindName("Properties");
        panel.Children.OfType<Button>().Single(b => Equals(b.Content, "チェックした辺を追加")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (border.Edges.Count != 4) throw new Exception("Selected center border bulk operation failed");
        Call("Undo");
        scene = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        if (scene.Find(border.Id)!.Edges.Count != 0) throw new Exception("Center border undo failed");
        Set("savedState", scene.Snapshot());
        Console.WriteLine("PASS: Center border drag, rendering, rotated hit testing, PNG/PDF, persistence, resize, degenerate grids, range tools and undo");
    }
}
