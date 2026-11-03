using System.Windows;

namespace GridSketch;

public sealed partial class SceneDocument
{
    public Rect GridContentExtent(SceneObject node, SceneObject grid)
    {
        var bounds = Rect.Empty;
        if (node.Kind == ObjectKind.GridText)
            foreach (var glyph in node.Glyphs) bounds.Union(CellRect(grid, new(glyph.Column, glyph.Row), false));
        if (node.Kind == ObjectKind.Border)
            foreach (var edge in BorderEdges(node, grid))
            {
                double offset = node.ConnectCellCenters ? 0.5 : 0;
                var start = new Point((edge.Column + offset) * grid.CellSize, (edge.Row + offset) * grid.CellSize);
                bounds.Union(new Rect(start, start + (edge.Vertical ? new Vector(0, grid.CellSize) : new Vector(grid.CellSize, 0))));
            }
        return bounds;
    }

    public static bool MovesCellContents(SceneObject node) => node.Kind is ObjectKind.Border or ObjectKind.GridText
        or ObjectKind.Fill or ObjectKind.CellFrame or ObjectKind.CellSelection;

    public void MoveCellContents(SceneObject node, SceneObject source, SceneObject grid, Vector delta)
    {
        double size = grid.CellSize;
        int dx = (int)Math.Round((source.X + delta.X) / size, MidpointRounding.AwayFromZero);
        int dy = (int)Math.Round((source.Y + delta.Y) / size, MidpointRounding.AwayFromZero);
        var bounds = GridContentExtent(source, grid);
        IEnumerable<CellPosition> cells = source.Kind switch
        {
            ObjectKind.Fill => source.FilledCells,
            ObjectKind.CellFrame => FramedCells(source, grid),
            ObjectKind.CellSelection => source.StateCells.Select(c => new CellPosition(c.Column, c.Row)),
            _ => []
        };
        foreach (var cell in cells) bounds.Union(CellRect(grid, cell, false));
        if (!bounds.IsEmpty)
        {
            dx = Math.Clamp(dx, (int)Math.Ceiling(-bounds.Left / size), (int)Math.Floor(grid.Columns - bounds.Right / size));
            dy = Math.Clamp(dy, (int)Math.Ceiling(-bounds.Top / size), (int)Math.Floor(grid.Rows - bounds.Bottom / size));
        }
        node.X = node.Y = 0;
        if (node.Kind == ObjectKind.Border)
        {
            node.Pattern = dx == 0 && dy == 0 ? source.Pattern : "個別";
            node.Edges = (node.Pattern == "個別" ? BorderEdges(source, grid) : source.Edges)
                .Select(e => new GridEdge(e.Column + dx, e.Row + dy, e.Vertical)).ToHashSet();
        }
        if (node.Kind == ObjectKind.GridText)
            node.Glyphs = source.Glyphs.Select(g => new Glyph { Column = g.Column + dx, Row = g.Row + dy,
                Text = g.Text, Font = g.Font, Size = g.Size, InCellText = g.InCellText }).ToList();
        if (node.Kind == ObjectKind.Fill) node.FilledCells = source.FilledCells.Select(c => new CellPosition(c.Column + dx, c.Row + dy)).ToHashSet();
        if (node.Kind == ObjectKind.CellFrame)
        {
            node.FrameAllCells = source.FrameAllCells && dx == 0 && dy == 0;
            node.FrameCells = (node.FrameAllCells ? source.FrameCells : FramedCells(source, grid))
                .Select(c => new CellPosition(c.Column + dx, c.Row + dy)).ToHashSet();
        }
        if (node.Kind == ObjectKind.CellSelection)
            node.StateCells = source.StateCells.Select(c => new CellStatePlacement { Column = c.Column + dx, Row = c.Row + dy, StateId = c.StateId }).ToList();
    }

    public void MoveOnGrid(SceneObject node, Point origin, Vector delta, SceneObject? contentOrigin = null)
    {
        if (MovesCellContents(node) && GridFor(node) is { } contentGrid)
        {
            MoveCellContents(node, contentOrigin ?? Restore(Snapshot()).Find(node.Id)!, contentGrid, delta); return;
        }
        var parent = Parent(node);
        var grid = parent;
        while (grid is not null && grid.Kind != ObjectKind.Grid) grid = Parent(grid);
        if (grid is not null && parent is not null)
        {
            var toGrid = World(parent); var inverseGrid = World(grid); inverseGrid.Invert(); toGrid.Append(inverseGrid);
            var gridDelta = toGrid.Transform(delta); double step = grid.CellSize / 2;
            gridDelta = new Vector(Math.Round(gridDelta.X / step, MidpointRounding.AwayFromZero) * step,
                Math.Round(gridDelta.Y / step, MidpointRounding.AwayFromZero) * step);
            toGrid.Invert(); delta = toGrid.Transform(gridDelta);
            var extent = GridContentExtent(node, grid);
            if (!extent.IsEmpty)
            {
                // Grid-dependent layers have translation only, including their organizing ancestors.
                delta.X = Math.Clamp(delta.X, Math.Ceiling((-extent.Left - origin.X) / step) * step,
                    Math.Floor((grid.Columns * grid.CellSize - extent.Right - origin.X) / step) * step);
                delta.Y = Math.Clamp(delta.Y, Math.Ceiling((-extent.Top - origin.Y) / step) * step,
                    Math.Floor((grid.Rows * grid.CellSize - extent.Bottom - origin.Y) / step) * step);
            }
        }
        node.X = origin.X + delta.X; node.Y = origin.Y + delta.Y;
    }
}
