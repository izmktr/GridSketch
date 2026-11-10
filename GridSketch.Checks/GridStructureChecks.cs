using System.Reflection;
using GridSketch;

internal static class GridStructureChecks
{
    public static void Run(MainWindow window)
    {
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        foreach (bool rows in new[] { true, false })
        {
            var scene = new SceneDocument();
            var grid = SceneDocument.CreateGrid("基本の方眼紙", 4, 4);
            scene.Canvas.Children.Add(grid);
            CellPosition Cell(int value) => rows ? new(1, value) : new(value, 1);
            var cell = Cell(2);
            var text = grid.Children.Single(n => n.Kind == ObjectKind.GridText);
            text.Glyphs.Add(new() { Column = cell.Column, Row = cell.Row, Text = "字", Size = 17 });
            var fill = new SceneObject { Kind = ObjectKind.Fill, Visible = false, FilledCells = [cell] };
            var selector = new SceneObject { Kind = ObjectKind.CellSelection };
            var template = new SceneObject { Kind = ObjectKind.Shape, X = 7, Y = 9 };
            selector.Children.Add(template); SceneDocument.AssignNewState(selector, template);
            SceneDocument.SetCellState(selector, cell, selector.States[0].Id);
            var frame = new SceneObject { Kind = ObjectKind.CellFrame, FrameAllCells = false, FrameCells = [cell] };
            var image = new SceneObject { Kind = ObjectKind.Image, SnapToCell = true, Column = cell.Column, Row = cell.Row };
            grid.Children.AddRange([fill, selector, frame, image]);
            scene.Validate();
            string original = scene.Snapshot();
            scene.EditGridStructure(grid, rows, 1, 2, false);
            var shifted = Cell(4);
            Check((rows ? grid.Rows : grid.Columns) == 6 && text.Glyphs.Single().Column == shifted.Column
                && text.Glyphs.Single().Row == shifted.Row && text.Glyphs.Single().Size == 17
                && fill.FilledCells.SetEquals([shifted]) && frame.FrameCells.SetEquals([shifted])
                && selector.StateCells.Single().Column == shifted.Column && selector.StateCells.Single().Row == shifted.Row
                && image.Column == shifted.Column && image.Row == shifted.Row && template.X == 7 && template.Y == 9,
                "Insert must shift all cell content and preserve template offsets and formatting");
            scene.EditGridStructure(grid, rows, 1, 2, true);
            Check(scene.Snapshot() == original, "Deleting newly inserted blank lines restores all original content");
            scene.EditGridStructure(grid, rows, 2, 1, true);
            Check(text.Glyphs.Count == 0 && fill.FilledCells.Count == 0 && frame.FrameCells.Count == 0
                && selector.StateCells.Count == 0 && selector.Children.Count == 1 && scene.Find(image.Id) is null,
                "Deleted cells remove placements and snapped images while retaining state templates");
            string before = scene.Snapshot();
            try { scene.EditGridStructure(grid, rows, 0, 3, true); throw new Exception("Accepted zero-sized grid"); }
            catch (InvalidOperationException) { }
            Check(scene.Snapshot() == before, "Invalid deletion must be atomic");
            text.Locked = true;
            try { scene.EditGridStructure(grid, rows, 0, 1, false); throw new Exception("Modified locked content"); }
            catch (InvalidOperationException) { }
        }
        var doc = new SceneDocument();
        var target = SceneDocument.CreateGrid("空のグリッド", 4, 4);
        doc.Canvas.Children.Add(target);
        var border = new SceneObject { Kind = ObjectKind.Border, Edges = [new(1, 1, true), new(1, 2, false), new(1, 3, false)] };
        target.Children.Add(border);
        doc.EditGridStructure(target, true, 2, 1, true);
        Check(border.Edges.SetEquals([new(1, 1, true), new(1, 2, false)]), "Deleting rows joins boundaries without duplicates");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MainWindow).GetField("document", flags)!.SetValue(window, doc);
        typeof(MainWindow).GetField("selectedId", flags)!.SetValue(window, target.Id);
        typeof(MainWindow).GetMethod("RefreshAll", flags)!.Invoke(window, null);
        string snapshot = doc.Snapshot();
        typeof(MainWindow).GetMethod("Change", flags)!.Invoke(window, [new Action(() => doc.EditGridStructure(target, false, 1, 2, false))]);
        typeof(MainWindow).GetMethod("Undo", flags)!.Invoke(window, null);
        var restored = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        Check(restored.Snapshot() == snapshot, "A structural edit must undo in one step");
        Console.WriteLine("PASS: Row/column insertion, deletion, cell content, templates, borders, locks, limits and undo");
    }
}
