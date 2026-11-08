using System.IO;
using System.Windows;
using GridSketch;

internal static class GridMovementChecks
{
    public static void Run(string directory)
    {
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        var scene = new SceneDocument(); var grid = SceneDocument.CreateGrid("空のグリッド", 4, 4, 40);
        scene.Canvas.Children.Add(grid);
        var text = new SceneObject { Kind = ObjectKind.GridText, Glyphs = [new() { Column = 1, Row = 1, Text = "A" }] };
        grid.Children.Add(text);
        var originalText = SceneDocument.Restore(scene.Snapshot()).Find(text.Id)!;
        scene.MoveOnGrid(text, new Point(), new Vector(31, -15));
        Check(text.X == 0 && text.Y == 0 && text.Glyphs[0].Column == 2 && text.Glyphs[0].Row == 1, "Grid text moves cell contents by whole-cell increments"); scene.Validate();
        scene.MoveOnGrid(text, new Point(), new Vector(45, 0), originalText);
        Check(text.Glyphs[0].Column == 2, "Repeated mouse movement uses the drag origin without accumulating shifts");
        scene.MoveOnGrid(text, new Point(), new Vector(0, 0), originalText);
        Check(text.Glyphs[0].Column == 1, "Dragging back restores the original cell coordinates");
        scene.MoveOnGrid(text, new Point(text.X, text.Y), new Vector(500, -500));
        Check(text.X == 0 && text.Y == 0 && text.Glyphs[0].Column == 3 && text.Glyphs[0].Row == 0, "Text movement clamps its occupied cells inside the grid"); scene.Validate();
        var border = new SceneObject { Kind = ObjectKind.Border, Pattern = "個別", Edges = [new(1, 1, false)] };
        grid.Children.Add(border); scene.MoveOnGrid(border, new Point(), new Vector(-500, 500));
        Check(border.X == 0 && border.Y == 0 && border.Edges.Contains(new GridEdge(0, 4, false)), "Border movement clamps centerline endpoints inside the grid"); scene.Validate();
        var fill = new SceneObject { Kind = ObjectKind.Fill, FilledCells = [new(1, 1)] }; grid.Children.Add(fill);
        scene.MoveOnGrid(fill, new Point(), new Vector(40, 40));
        Check(fill.X == 0 && fill.Y == 0 && fill.FilledCells.SetEquals([new CellPosition(2, 2)]), "Fill movement shifts stored cells");
        var selector = new SceneObject { Kind = ObjectKind.CellSelection };
        var template = new SceneObject { Kind = ObjectKind.Shape }; selector.Children.Add(template); grid.Children.Add(selector);
        SceneDocument.AssignNewState(selector, template);
        SceneDocument.SetCellState(selector, new(1, 1), selector.States[0].Id);
        scene.MoveOnGrid(selector, new Point(), new Vector(40, 0)); scene.Validate();
        Check(selector.StateCells[0].Column == 2 && selector.StateCells[0].StateId == selector.States[0].Id,
            "Moving state cells preserves state and template identities");
        var full = new SceneObject { Kind = ObjectKind.Border, Pattern = "外枠" }; grid.Children.Add(full);
        scene.MoveOnGrid(full, new Point(), new Vector(30, 30)); Check(full.X == 0 && full.Y == 0, "Full perimeter cannot leave its grid");
        var image = new SceneObject { Kind = ObjectKind.Image, SnapToCell = true }; grid.Children.Add(image);
        scene.MoveOnGrid(image, new Point(), new Vector(25, 45));
        Check(image.X == 25 && image.Y == 45, "Cell images move on integer coordinates"); scene.Validate();
        var group = new SceneObject { Kind = ObjectKind.Group, Angle = 90 };
        var shape = new SceneObject { Kind = ObjectKind.Shape }; grid.Children.Add(group); group.Children.Add(shape);
        scene.MoveOnGrid(shape, new Point(), new Vector(31, 0));
        Check(Math.Abs(shape.X - 31) < 0.001 && Math.Abs(shape.Y) < 0.001, "Nested rotated groups retain integer local coordinates");
        var free = new SceneObject { Kind = ObjectKind.Shape }; scene.Canvas.Children.Add(free);
        scene.MoveOnGrid(free, new Point(), new Vector(13, 17)); Check(free.X == 13 && free.Y == 17, "Objects without a parent grid move freely");
        scene.MoveOnGrid(free, new Point(0.3, -0.2), new Vector(2.3, -2.4));
        Check(free.X == 3 && free.Y == -3, "Final coordinates round to nearest integers including fractional origins and negative values");
        foreach (var (zoom, expected) in new[] { (0.5, 1.0), (1.0, 1.0), (1.5, 1.5), (2.0, 1.5), (3.0, 1.25), (4.0, 1.25), (8.0, 1.375) })
        {
            scene.MoveOnGrid(free, new Point(), new Vector(1.37, -1.37), zoom: zoom);
            Check(free.X == expected && free.Y == -expected, "Zoom must select a power-of-two fractional coordinate step");
        }
        group.PreservedTransform = new() { M11 = 4, M22 = 4 }; group.Angle = 0;
        scene.MoveOnGrid(shape, new Point(), new Vector(1.37, -1.37));
        Check(shape.X == 1.25 && shape.Y == -1.25, "Parent magnification also permits fractional coordinate movement");
        scene.MoveOnGrid(text, new Point(), new Vector(-12, 0), zoom: 8);
        Check(text.Glyphs[0].Column == 3, "Zoom does not subdivide grid-content cell movement");
        var surface = new SceneSurface { Selected = text }; surface.Refresh(scene);
        var world = scene.World(text).Transform(new Point(60, 60));
        Check(surface.CellAt(grid, new Point(world.X + surface.OriginX, world.Y + surface.OriginY)) == new CellPosition(1, 1), "Moved text hit testing follows its offset");
        string file = Path.Combine(directory, "grid-movement.gridsketch"); scene.Save(file);
        Check(SceneDocument.Load(file).Find(text.Id)!.Glyphs[0].Column == 3, "Moved cell contents persist");
        text.X += 40; bool rejected = false;
        try { scene.Validate(); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Out-of-grid text offsets are rejected");
        Console.WriteLine("PASS: Coordinate snapping, borders/text bounds, images, nested transforms, free movement, hit testing and persistence");
    }
}
