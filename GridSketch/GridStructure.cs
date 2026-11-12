namespace GridSketch;

public sealed partial class SceneDocument
{
    public void EditGridStructure(SceneObject grid, bool rows, int start, int count, bool delete)
    {
        if (Find(grid.Id) != grid || grid.Kind != ObjectKind.Grid || !Editable(grid))
            throw new InvalidOperationException("編集可能なグリッドを選択してください。");
        int size = rows ? grid.Rows : grid.Columns;
        int next = size + (delete ? -count : count);
        if (count < 1 || start < 0 || start > size || delete && start + count > size || next is < 1 or > 200)
            throw new InvalidOperationException("行数・列数は1～200の範囲で変更してください。");
        if (GridMembers(grid).Any(n => !Editable(n)))
            throw new InvalidOperationException("グリッド内のロックを解除してから行・列を変更してください。");
        var trial = Restore(Snapshot());
        trial.EditGridStructureCore(trial.Find(grid.Id)!, rows, start, count, delete);
        trial.Validate();
        EditGridStructureCore(grid, rows, start, count, delete);
    }

    private void EditGridStructureCore(SceneObject grid, bool rows, int start, int count, bool delete)
    {
        int Map(int value) => value < start ? value : value + (delete ? -count : count);
        bool Removed(int value) => delete && value >= start && value < start + count;
        int Index(CellPosition cell) => rows ? cell.Row : cell.Column;
        CellPosition MapCell(CellPosition cell) => rows ? cell with { Row = Map(cell.Row) } : cell with { Column = Map(cell.Column) };
        HashSet<CellPosition> MapCells(IEnumerable<CellPosition> cells) =>
            cells.Where(c => !Removed(Index(c))).Select(MapCell).ToHashSet();
        GridEdge? MapEdge(GridEdge edge, bool centers)
        {
            int value = rows ? edge.Row : edge.Column;
            bool alongAxis = edge.Diagonal != 0 || rows == edge.Vertical;
            int mapped;
            if (centers)
            {
                // A center-to-center segment requires both of its original cells.
                if (Removed(value) || alongAxis && Removed(value + 1)) return null;
                if (!delete && alongAxis && value < start && value + 1 >= start) return null;
                mapped = Map(value);
            }
            else if (alongAxis)
            {
                if (Removed(value)) return null;
                mapped = Map(value);
            }
            else
            {
                // Keep the leading boundary when deletion joins two boundaries.
                if (delete && value > start && value <= start + count) return null;
                mapped = value <= start ? value : Map(value);
            }
            return rows ? edge with { Row = mapped } : edge with { Column = mapped };
        }

        foreach (var node in GridMembers(grid).ToList())
        {
            if (UsesGridCoordinates(node))
            {
                double coordinate = (rows ? node.GridRow : node.GridColumn)!.Value - 1;
                if (delete && coordinate >= start && coordinate < start + count)
                { Parent(node)!.Children.Remove(node); continue; }
                if (coordinate >= start)
                {
                    if (rows) node.GridRow += delete ? -count : count;
                    else node.GridColumn += delete ? -count : count;
                }
            }
            node.Glyphs.RemoveAll(g => Removed(rows ? g.Row : g.Column));
            foreach (var glyph in node.Glyphs)
            {
                if (rows) glyph.Row = Map(glyph.Row); else glyph.Column = Map(glyph.Column);
            }
            node.FilledCells = MapCells(node.FilledCells);
            node.FrameCells = MapCells(node.FrameCells);
            node.RegionLinks = node.RegionLinks.Select(edge => RegionNeighbors(edge))
                .Where(pair => !Removed(Index(pair.First)) && !Removed(Index(pair.Second)))
                .Select(pair => (First: MapCell(pair.First), Second: MapCell(pair.Second)))
                .Where(pair => Math.Abs(pair.First.Column - pair.Second.Column) + Math.Abs(pair.First.Row - pair.Second.Row) == 1)
                .Select(pair => RegionLink(pair.First, pair.Second)).ToHashSet();
            node.StateCells.RemoveAll(c => Removed(rows ? c.Row : c.Column));
            foreach (var cell in node.StateCells)
            {
                if (rows) cell.Row = Map(cell.Row); else cell.Column = Map(cell.Column);
            }
            node.Edges = node.Edges.Select(e => MapEdge(e, node.ConnectCellCenters))
                .Where(e => e.HasValue).Select(e => e!.Value).ToHashSet();
            node.StateEdges = node.StateEdges.Select(e => (Placement: e, Edge: MapEdge(e.Edge, false)))
                .Where(e => e.Edge.HasValue)
                .Select(e => new EdgeStatePlacement { Edge = e.Edge!.Value, StateId = e.Placement.StateId }).ToList();
            if (node.Kind == ObjectKind.Image && node.SnapToCell)
            {
                if (Removed(rows ? node.Row : node.Column)) Parent(node)!.Children.Remove(node);
                else if (rows) node.Row = Map(node.Row);
                else node.Column = Map(node.Column);
            }
        }
        if (rows) grid.Rows += delete ? -count : count;
        else grid.Columns += delete ? -count : count;
    }
}
