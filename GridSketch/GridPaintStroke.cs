using System.Windows;

namespace GridSketch;

// Points are expressed in grid-local pixels, independent of zoom and parent rotation.
public sealed class GridPaintStroke
{
    private readonly SceneObject node, grid;
    private readonly SceneDocument document;
    private CellPosition? lastRegionCell;
    private readonly bool adding;
    private readonly string? stateId;
    private Point? previous;
    private GridEdge? currentEdge;
    public GridPaintStroke(SceneDocument document, SceneObject node, SceneObject grid, Point start, GridEdge? edge = null)
    {
        this.node = node; this.grid = grid; this.document = document;
        if (node.Kind == ObjectKind.EdgeSelection)
        {
            currentEdge = edge ?? throw new ArgumentException("開始辺が必要です。");
            stateId = SceneDocument.NextEdgeState(node, edge.Value); PaintEdge(edge.Value);
        }
        else if (node.Kind == ObjectKind.CellFrame && edge is { } regionEdge)
        {
            currentEdge = regionEdge;
            adding = !node.RegionLinks.Contains(regionEdge);
            PaintEdge(regionEdge);
        }
        else if (node.Kind == ObjectKind.Border)
        {
            currentEdge = edge ?? throw new ArgumentException("開始辺が必要です。");
            node.Edges = SceneDocument.BorderEdges(node, grid).ToHashSet(); node.Pattern = "個別";
            adding = !node.Edges.Contains(edge.Value); PaintEdge(edge.Value);
        }
        else
        {
            var cell = Cell(Normalize(start));
            if (node.Kind == ObjectKind.CellSelection) stateId = SceneDocument.NextCellState(node, cell);
            else if (node.Kind == ObjectKind.CellFrame)
            {
                adding = !SceneDocument.FramedCells(node, grid).Contains(cell);
                document.SetFrameCells(node, [cell], adding);
            }
            else adding = !node.FilledCells.Contains(cell);
            PaintCell(cell);
        }
        previous = Normalize(start);
    }
    private Point Normalize(Point p) => new(p.X / grid.CellSize - (node.ConnectCellCenters ? 0.5 : 0), p.Y / grid.CellHeight - (node.ConnectCellCenters ? 0.5 : 0));
    private static CellPosition Cell(Point p) => new((int)Math.Floor(p.X), (int)Math.Floor(p.Y));
    private void PaintCell(CellPosition cell)
    {
        if (cell.Column < 0 || cell.Row < 0 || cell.Column >= grid.Columns || cell.Row >= grid.Rows) { lastRegionCell = null; return; }
        if (node.Kind == ObjectKind.CellSelection) { SceneDocument.SetCellState(node, cell, stateId); return; }
        if (node.Kind == ObjectKind.CellFrame)
        {
            document.SetFrameCells(node, [cell], adding);
            if (adding && lastRegionCell is { } previousCell
                && Math.Abs(previousCell.Column - cell.Column) + Math.Abs(previousCell.Row - cell.Row) == 1)
                document.SetRegionLink(node, SceneDocument.RegionLink(previousCell, cell), true);
            lastRegionCell = cell;
            return;
        }
        var cells = node.Kind == ObjectKind.CellFrame ? node.FrameCells : node.FilledCells;
        if (adding) cells.Add(cell); else cells.Remove(cell);
    }
    private void PaintEdge(GridEdge edge) { if (node.Kind == ObjectKind.CellFrame) { document.SetRegionLink(node, edge, adding); return; } if (node.Kind == ObjectKind.EdgeSelection) { if (SceneDocument.EdgeSelectable(node, edge)) SceneDocument.SetEdgeState(node, edge, stateId); return; } if (adding) node.Edges.Add(edge); else node.Edges.Remove(edge); }
    public void Move(Point local)
    {
        var end = Normalize(local);
        var start = previous ?? end;
        // Sample densely enough to bridge missing mouse events, without unbounded work outside the grid.
        int count = Math.Clamp((int)Math.Min(6000, Math.Ceiling((end - start).Length * 10)), 1, 6000);
        for (int i = 1; i <= count; i++)
        {
            var p = start + (end - start) * ((double)i / count);
            if (currentEdge is not null) FollowEdge(p); else PaintCell(Cell(p));
        }
        previous = end.X >= -0.3 && end.Y >= -0.3 && end.X <= grid.Columns + 0.3 && end.Y <= grid.Rows + 0.3 ? end : null;
    }
    private void FollowEdge(Point p)
    {
        var current = currentEdge!.Value;
        var first = new Point(current.Column, current.Row);
        var second = first + (current.Vertical ? new Vector(0, 1) : new Vector(1, 0));
        GridEdge? best = null; double bestDistance = 0.28;
        foreach (var vertex in new[] { first, second })
        foreach (var direction in new[] { new Vector(1, 0), new Vector(-1, 0), new Vector(0, 1), new Vector(0, -1) })
        {
            var other = vertex + direction;
            var candidate = new GridEdge((int)Math.Min(vertex.X, other.X), (int)Math.Min(vertex.Y, other.Y), direction.X == 0);
            if (node.Kind == ObjectKind.EdgeSelection && !SceneDocument.EdgeSelectable(node, candidate)) continue;
            if (candidate == current || !SceneDocument.BorderEdgeInside(node, grid, candidate)) continue;
            var delta = p - vertex;
            double progress = Vector.Multiply(delta, direction);
            // Require deliberate movement away from the shared vertex before changing edges.
            if (progress < 0.3 || progress > 1.2) continue;
            double distance = (delta - direction * Math.Clamp(progress, 0, 1)).Length;
            if (distance < bestDistance) { bestDistance = distance; best = candidate; }
        }
        if (best is { } next) { PaintEdge(next); currentEdge = next; }
    }
}
