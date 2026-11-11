using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using GridSketch;

internal static class GridCoordinateChecks
{
    public static void Run(MainWindow window, string directory)
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        var doc = new SceneDocument();
        var grid = SceneDocument.CreateGrid("空のグリッド", 8, 8, 40);
        grid.SeparateCellHeight = 60; grid.Margin = 5; grid.MarginY = 7;
        doc.Canvas.Children.Add(grid);
        var piece = new SceneObject { Kind = ObjectKind.Shape, Width = 10, Height = 20, Thickness = 0 };
        grid.Children.Add(piece); SceneDocument.InitializeGridCoordinates(piece, 3, 5);
        piece.LayoutSizing = "固定"; piece.LayoutAnchor = "左上"; piece.PlacementOffsetX = piece.PlacementOffsetY = 2;
        Check(doc.World(piece).Transform(new Point()) == new Point(87, 249), "Cell coordinates must combine inset, alignment and pixel offset");
        var origin = new Point(3, 5);
        doc.MoveOnGrid(piece, origin, new Vector(11, -17), zoom: 8);
        Check(piece.GridColumn == 3.5 && piece.GridRow == 4.5 && piece.X == 0 && piece.Y == 0
            && piece.PlacementOffsetX == 2, "Dragging must snap each axis to half a cell and preserve placement offset");
        var other = SceneDocument.CreateGrid("空のグリッド", 8, 8, 80); other.X = 500; doc.Canvas.Children.Add(other);
        doc.ReparentMany([piece], other);
        Check(piece.GridColumn == 3.5 && piece.GridRow == 4.5 && piece.LayoutAnchor == "左上"
            && doc.World(piece).Transform(new Point()).X == 707, "Changing grids preserves coordinate and placement values relative to destination");
        var world = doc.World(piece);
        doc.ReparentMany([piece], doc.Canvas);
        Check(!piece.GridColumn.HasValue && doc.World(piece) == world, "Leaving grid preserves pixel placement");
        doc.ReparentMany([piece], grid);
        Check(doc.UsesGridCoordinates(piece) && doc.World(piece).Transform(new Point()) == world.Transform(new Point()),
            "Entering grid adopts coordinates without a positional jump");
        var legacy = new SceneObject { Kind = ObjectKind.Text, Text = "駒", X = 35, Y = 71, Angle = 20 };
        grid.Children.Add(legacy);
        var before = new SceneRenderer(doc).Bounds[legacy.Id];
        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(100, 50, 96, 96, System.Windows.Media.PixelFormats.Bgra32,
            null, Enumerable.Repeat((byte)255, 100 * 50 * 4).ToArray(), 400);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(); encoder.Save(stream);
        var image = new SceneObject { Kind = ObjectKind.Image, SnapToCell = true, Column = 2, Row = 3, ImageData = stream.ToArray() };
        grid.Children.Add(image);
        var imageBefore = new SceneRenderer(doc).Bounds[image.Id];
        string file = Path.Combine(directory, "grid-coordinates.gridsketch"); doc.Save(file);
        var loaded = SceneDocument.Load(file);
        var after = new SceneRenderer(loaded).Bounds[legacy.Id];
        Check((before.TopLeft - after.TopLeft).Length < 0.001 && Math.Abs(before.Width - after.Width) < 0.001
            && loaded.UsesGridCoordinates(loaded.Find(legacy.Id)!) && loaded.Find(legacy.Id)!.Angle == 20, "Legacy file conversion must preserve rendered bounds and rotation");
        var imageAfter = new SceneRenderer(loaded).Bounds[image.Id];
        Check((imageBefore.TopLeft - imageAfter.TopLeft).Length < 0.001 && Math.Abs(imageBefore.Width - imageAfter.Width) < 0.001
            && loaded.UsesGridCoordinates(loaded.Find(image.Id)!), "Legacy snapped images retain appearance after conversion");
        piece.GridColumn = 2; piece.GridRow = 3;
        doc.EditGridStructure(grid, true, 1, 2, false);
        Check(piece.GridRow == 5, "Inserted rows shift cell-coordinate objects");
        doc.EditGridStructure(grid, true, 4, 1, true);
        Check(doc.Find(piece.Id) is null, "Deleted rows remove placements anchored in removed cells");
        var line = new SceneObject { Kind = ObjectKind.Line, EndX = 20, EndY = 10 };
        grid.Children.Add(line); SceneDocument.InitializeGridCoordinates(line);
        var initialStart = doc.World(line).Transform(new Point());
        var drag = new LineHandleDrag(doc, line, LineHandle.End); drag.Move(new Vector(10, 0), 1);
        Check(doc.UsesGridCoordinates(line) && (doc.World(line).Transform(new Point()) - initialStart).Length < 0.001,
            "Line handle editing must preserve untouched points with grid coordinates");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MainWindow).GetField("document", flags)!.SetValue(window, doc);
        typeof(MainWindow).GetField("selectedId", flags)!.SetValue(window, line.Id);
        typeof(MainWindow).GetMethod("RefreshAll", flags)!.Invoke(window, null);
        var panel = (StackPanel)window.FindName("Properties");
        PropertyChecks.Find<TextBox>(panel, "列（マス座標）").Text = "2.5";
        PropertyChecks.Find<TextBox>(panel, "ずらし量 X（px）").Text = "3";
        LivePropertyChecks.Commit(window);
        Check(line.GridColumn == 2.5 && line.PlacementOffsetX == 3, "Coordinate and placement editors change independent values");
        var organizer = new SceneObject { Kind = ObjectKind.Group, OrganizeOnly = true };
        var nestedOrganizer = new SceneObject { Kind = ObjectKind.Group, OrganizeOnly = true };
        other.Children.Add(organizer); organizer.Children.Add(nestedOrganizer);
        var child = new SceneObject { Kind = ObjectKind.Shape, Width = 10, Height = 20 };
        nestedOrganizer.Children.Add(child); SceneDocument.InitializeGridCoordinates(child, 2, 3);
        child.LayoutSizing = "固定"; child.LayoutAnchor = "左上"; child.PlacementOffsetX = 2;
        Check(doc.UsesGridCoordinates(child) && doc.CoordinateGrid(nestedOrganizer) == other
            && doc.ParentRegions(child).Outer == new Rect(80, 160, 80, 80)
            && doc.World(child).Transform(new Point()) == new Point(587, 165), "Nested organizers must pass through the child's grid cell and placement");
        doc.MoveOnGrid(child, new Point(2, 3), new Vector(40, 40));
        Check(child.GridColumn == 2.5 && child.GridRow == 3.5, "Nested organizers must use grid dimensions for half-cell snapping");
        doc.ReparentMany([child], grid);
        Check(child.GridColumn == 2.5 && child.GridRow == 3.5, "Moving out of organizer must preserve cell coordinates");
        doc.ReparentMany([child], nestedOrganizer);
        var childWorld = doc.World(child);
        doc.EnableGroupTransform(organizer);
        Check(!doc.UsesGridCoordinates(child) && doc.World(child) == childWorld, "Transform groups must define their own coordinates while preserving appearance");
        var oldNested = new SceneObject { Kind = ObjectKind.Shape, X = 17, Y = 23 };
        var oldOrganizer = new SceneObject { Kind = ObjectKind.Group, OrganizeOnly = true, Children = [oldNested] };
        grid.Children.Add(oldOrganizer);
        doc.Save(file);
        var reopened = SceneDocument.Load(file);
        Check(reopened.UsesGridCoordinates(reopened.Find(oldNested.Id)!), "Loading legacy children of organizers must adopt grid coordinates");
        doc.Validate();
        Console.WriteLine("PASS: Grid coordinates, half-cell snapping, placement offsets, reparenting, legacy import, structural edits and line handles");
    }
}
