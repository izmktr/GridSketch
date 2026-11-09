using System.Windows;

namespace GridSketch;

public sealed partial class SceneDocument
{
    public Rect GridContentExtent(SceneObject node, SceneObject grid)
    {
        var bounds = Rect.Empty;
        if (node.Kind == ObjectKind.GridText)
            foreach (var glyph in node.Glyphs) bounds.Union(CellRect(grid, new(glyph.Column, glyph.Row), false));
        if (node.Kind is ObjectKind.Border or ObjectKind.EdgeSelection)
            foreach (var edge in node.Kind == ObjectKind.EdgeSelection ? node.StateEdges.Select(e => e.Edge) : BorderEdges(node, grid))
            {
                double offset = node.ConnectCellCenters ? 0.5 : 0;
                var start = new Point((edge.Column + offset) * grid.CellSize, (edge.Row + offset) * grid.CellSize);
                bounds.Union(new Rect(start, start + (edge.Vertical ? new Vector(0, grid.CellSize) : new Vector(grid.CellSize, 0))));
            }
        return bounds;
    }

    public static bool MovesCellContents(SceneObject node) => node.Kind is ObjectKind.Border or ObjectKind.GridText
        or ObjectKind.Fill or ObjectKind.CellFrame or ObjectKind.CellSelection or ObjectKind.EdgeSelection;

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
        if (node.Kind == ObjectKind.EdgeSelection)
            node.StateEdges = source.StateEdges.Select(p => new EdgeStatePlacement { Edge = new GridEdge(p.Edge.Column + dx, p.Edge.Row + dy, p.Edge.Vertical), StateId = p.StateId }).ToList();
        if (node.Kind == ObjectKind.CellSelection)
            node.StateCells = source.StateCells.Select(c => new CellStatePlacement { Column = c.Column + dx, Row = c.Row + dy, StateId = c.StateId }).ToList();
    }

    public void MoveOnGrid(SceneObject node, Point origin, Vector delta, SceneObject? contentOrigin = null, double zoom = 1)
    {
        if (MovesCellContents(node) && GridFor(node) is { } contentGrid)
        {
            MoveCellContents(node, contentOrigin ?? Restore(Snapshot()).Find(node.Id)!, contentGrid, delta); return;
        }
        var parentMatrix = Parent(node) is { } parent ? World(parent) : System.Windows.Media.Matrix.Identity;
        double Step(double scale)
        {
            double magnification = zoom * scale;
            return magnification <= 1 ? 1 : Math.Pow(2, -Math.Ceiling(Math.Log2(magnification)));
        }
        double stepX = Step(new Vector(parentMatrix.M11, parentMatrix.M12).Length);
        double stepY = Step(new Vector(parentMatrix.M21, parentMatrix.M22).Length);
        // Snap the resulting coordinates, not just the drag delta: fractional starting
        // positions must also land on the current coordinate lattice.
        node.X = Math.Round((origin.X + delta.X) / stepX, MidpointRounding.AwayFromZero) * stepX;
        node.Y = Math.Round((origin.Y + delta.Y) / stepY, MidpointRounding.AwayFromZero) * stepY;
    }
}
