using System.IO;
using System.Windows;
using System.Windows.Media;

namespace GridSketch;

public sealed partial class SceneDocument
{
    public static (CellPosition First, CellPosition Second) RegionNeighbors(GridEdge edge) => edge.Vertical
        ? (new(edge.Column - 1, edge.Row), new(edge.Column, edge.Row))
        : (new(edge.Column, edge.Row - 1), new(edge.Column, edge.Row));

    public static GridEdge RegionLink(CellPosition first, CellPosition second) => first.Row == second.Row
        ? new(Math.Max(first.Column, second.Column), first.Row, true)
        : new(first.Column, Math.Max(first.Row, second.Row), false);

    public static void PruneRegionLinks(SceneObject frame, SceneObject grid)
    {
        var cells = FramedCells(frame, grid).ToHashSet();
        frame.RegionLinks.RemoveWhere(edge =>
        {
            var (first, second) = RegionNeighbors(edge);
            return !cells.Contains(first) || !cells.Contains(second);
        });
    }

    public void SetRegionLink(SceneObject frame, GridEdge edge, bool enabled)
    {
        var grid = GridFor(frame) ?? throw new InvalidOperationException("領域枠を選択してください。");
        var (first, second) = RegionNeighbors(edge);
        if (first.Column < 0 || first.Row < 0 || second.Column >= grid.Columns || second.Row >= grid.Rows) return;
        if (enabled)
        {
            SetFrameCells(frame, [first, second], true);
            frame.RegionLinks.Add(edge);
        }
        else frame.RegionLinks.Remove(edge);
    }

    public void JoinRegionCells(SceneObject frame, IEnumerable<CellPosition> selection)
    {
        var cells = selection.ToHashSet();
        SetFrameCells(frame, cells, true);
        foreach (var cell in cells)
        {
            var right = new CellPosition(cell.Column + 1, cell.Row);
            var below = new CellPosition(cell.Column, cell.Row + 1);
            if (cells.Contains(right)) frame.RegionLinks.Add(RegionLink(cell, right));
            if (cells.Contains(below)) frame.RegionLinks.Add(RegionLink(cell, below));
        }
    }

    public static IEnumerable<HashSet<CellPosition>> FrameRegions(SceneObject frame, SceneObject grid)
    {
        var remaining = FramedCells(frame, grid).ToHashSet();
        foreach (var start in remaining.ToArray())
        {
            if (!remaining.Remove(start)) continue;
            var region = new HashSet<CellPosition> { start };
            var queue = new Queue<CellPosition>(); queue.Enqueue(start);
            while (queue.TryDequeue(out var cell))
            {
                foreach (var next in new[] { new CellPosition(cell.Column - 1, cell.Row), new(cell.Column + 1, cell.Row),
                    new(cell.Column, cell.Row - 1), new(cell.Column, cell.Row + 1) })
                    if (remaining.Contains(next) && frame.RegionLinks.Contains(RegionLink(cell, next)))
                    { remaining.Remove(next); region.Add(next); queue.Enqueue(next); }
            }
            yield return region;
        }
    }

    private void ValidateRegionFrame(SceneObject node)
    {
        if (node.Kind != ObjectKind.CellFrame) return;
        _ = (Color)ColorConverter.ConvertFromString(node.RegionInnerColor);
        if (node.RegionLinks is null || !double.IsFinite(node.RegionInset) || Math.Abs(node.RegionInset) > 256
            || !double.IsFinite(node.RegionInnerThickness) || node.RegionInnerThickness < 0 || node.RegionInnerThickness > 64
            || node.RegionOuterStyle is not ("なし" or "実線" or "破線" or "点線")
            || node.RegionInnerStyle is not ("なし" or "実線" or "破線" or "点線"))
            throw new InvalidDataException("領域枠の線種・線幅・引き込みが不正です。");
        var cells = FramedCells(node, GridFor(node)!).ToHashSet();
        if (node.RegionLinks.Any(e => { var (a, b) = RegionNeighbors(e); return !cells.Contains(a) || !cells.Contains(b); }))
            throw new InvalidDataException("領域の結合は対象マス同士の間に指定してください。");
    }
}

public static class RegionFrameDrawing
{
    private static Pen Stroke(double width, string style) => new(Brushes.Black, width)
    {
        LineJoin = PenLineJoin.Miter,
        DashStyle = style switch { "破線" => DashStyles.Dash, "点線" => DashStyles.Dot, _ => DashStyles.Solid }
    };

    private static (Geometry Outer, Geometry Inner) Outlines(SceneObject frame, SceneObject grid)
    {
        var outer = new GeometryGroup { FillRule = FillRule.Nonzero };
        var inner = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var cells in SceneDocument.FrameRegions(frame, grid))
        {
            var rectangles = new GeometryGroup { FillRule = FillRule.Nonzero };
            foreach (var cell in cells) rectangles.Children.Add(new RectangleGeometry(SceneDocument.CellRect(grid, cell, false)));
            System.Windows.Media.Geometry outline = rectangles.GetOutlinedPathGeometry(0.02, ToleranceType.Absolute);
            if (frame.RegionInset != 0)
            {
                var rim = outline.GetWidenedPathGeometry(Stroke(Math.Abs(frame.RegionInset) * 2, "実線"), 0.02, ToleranceType.Absolute);
                outline = System.Windows.Media.Geometry.Combine(outline, rim,
                    frame.RegionInset > 0 ? GeometryCombineMode.Exclude : GeometryCombineMode.Union, null, 0.02, ToleranceType.Absolute);
            }
            if (frame.RegionOuterStyle != "なし" && frame.Thickness > 0)
                outer.Children.Add(outline.GetWidenedPathGeometry(Stroke(frame.Thickness, frame.RegionOuterStyle), 0.02, ToleranceType.Absolute));
            if (frame.RegionInnerStyle == "なし" || frame.RegionInnerThickness == 0) continue;
            var inside = new GeometryGroup { FillRule = FillRule.Nonzero };
            foreach (var cell in cells)
            {
                if (cells.Contains(new(cell.Column + 1, cell.Row)))
                    Add(new((cell.Column + 1) * grid.CellSize, cell.Row * grid.CellHeight), new((cell.Column + 1) * grid.CellSize, (cell.Row + 1) * grid.CellHeight));
                if (cells.Contains(new(cell.Column, cell.Row + 1)))
                    Add(new(cell.Column * grid.CellSize, (cell.Row + 1) * grid.CellHeight), new((cell.Column + 1) * grid.CellSize, (cell.Row + 1) * grid.CellHeight));
            }
            void Add(Point first, Point second) => inside.Children.Add(new LineGeometry(first, second)
                .GetWidenedPathGeometry(Stroke(frame.RegionInnerThickness, frame.RegionInnerStyle), 0.02, ToleranceType.Absolute));
            inner.Children.Add(frame.RegionInset > 0 ? System.Windows.Media.Geometry.Combine(inside, outline,
                GeometryCombineMode.Intersect, null, 0.02, ToleranceType.Absolute) : inside);
        }
        return (outer.GetOutlinedPathGeometry(0.02, ToleranceType.Absolute), inner.GetOutlinedPathGeometry(0.02, ToleranceType.Absolute));
    }

    public static Geometry Geometry(SceneObject frame, SceneObject grid)
    {
        var (outer, inner) = Outlines(frame, grid);
        return System.Windows.Media.Geometry.Combine(outer, inner, GeometryCombineMode.Union, null, 0.02, ToleranceType.Absolute);
    }

    public static DrawingGroup Drawing(SceneObject frame, SceneObject grid)
    {
        var (outer, inner) = Outlines(frame, grid);
        var outerColor = (Color)ColorConverter.ConvertFromString(frame.Color);
        var innerColor = (Color)ColorConverter.ConvertFromString(frame.RegionInnerColor);
        var drawing = new DrawingGroup();
        using var dc = drawing.Open();
        if (outerColor == innerColor)
            dc.DrawGeometry(new SolidColorBrush(outerColor), null, System.Windows.Media.Geometry.Combine(outer, inner,
                GeometryCombineMode.Union, null, 0.02, ToleranceType.Absolute));
        else
        {
            // Give the perimeter priority without accumulating opacity at intersections.
            dc.DrawGeometry(new SolidColorBrush(innerColor), null, System.Windows.Media.Geometry.Combine(inner, outer,
                GeometryCombineMode.Exclude, null, 0.02, ToleranceType.Absolute));
            dc.DrawGeometry(new SolidColorBrush(outerColor), null, outer);
        }
        return drawing;
    }
}
