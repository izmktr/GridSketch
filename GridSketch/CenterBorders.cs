using System.Windows;

namespace GridSketch;

public sealed partial class SceneDocument
{
    public static bool BorderDirectionAllowed(SceneObject node, GridEdge edge) => !node.ConnectCellCenters
        ? edge.Diagonal == 0 : node.CenterDirections == "縦横＋斜め" || (node.CenterDirections == "斜め") == (edge.Diagonal != 0);

    public static IEnumerable<GridEdge> EdgesAtVertex(int x, int y)
    {
        yield return new(x, y, false); yield return new(x - 1, y, false);
        yield return new(x, y, true); yield return new(x, y - 1, true);
        yield return new(x, y, false) { Diagonal = 1 }; yield return new(x - 1, y - 1, false) { Diagonal = 1 };
        yield return new(x - 1, y, false) { Diagonal = -1 }; yield return new(x, y - 1, false) { Diagonal = -1 };
    }

    public static GridEdge? CenterEdgeAt(SceneObject node, SceneObject grid, Point p, double zoom)
    {
        p.Offset(-grid.CellSize / 2, -grid.CellHeight / 2);
        int x = (int)Math.Floor(p.X / grid.CellSize), y = (int)Math.Floor(p.Y / grid.CellHeight);
        GridEdge? best = null;
        double distance = Math.Min(Math.Min(grid.CellSize, grid.CellHeight) * 0.22, 7 / zoom);
        for (int c = x; c <= x + 1; c++) for (int r = y; r <= y + 1; r++)
            foreach (var edge in EdgesAtVertex(c, r))
            {
                if (!BorderEdgeInside(node, grid, edge) || !BorderDirectionAllowed(node, edge)) continue;
                var start = new Point(edge.Start.X * grid.CellSize, edge.Start.Y * grid.CellHeight);
                var end = new Point(edge.End.X * grid.CellSize, edge.End.Y * grid.CellHeight);
                var d = end - start;
                var t = Math.Clamp(Vector.Multiply(p - start, d) / d.LengthSquared, 0, 1);
                double next = (p - (start + d * t)).Length;
                if (next < distance) { best = edge; distance = next; }
            }
        return best;
    }
}
