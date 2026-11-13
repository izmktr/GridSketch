using System.Windows;
using System.Windows.Media;

namespace GridSketch;

public sealed partial class SceneDocument
{
    public Point LineStart(SceneObject line)
        => UsesGridCoordinates(line) && CoordinateGrid(Parent(line)) is { } grid
            ? new((line.GridColumn!.Value - .5) * grid.CellSize, (line.GridRow!.Value - .5) * grid.CellHeight)
            : new(line.X, line.Y);

    public Point LinePoint(SceneObject line, LineHandle handle, bool displayUnits = true)
    {
        var start = LineStart(line);
        var point = handle switch
        {
            LineHandle.End => start + new Vector(line.EndX, line.EndY),
            LineHandle.Control => start + new Vector(line.ControlX, line.ControlY),
            _ => start
        };
        return displayUnits && UsesGridCoordinates(line) && CoordinateGrid(Parent(line)) is { } grid
            ? new(point.X / grid.CellSize + .5, point.Y / grid.CellHeight + .5) : point;
    }
    private void SetLineStart(SceneObject line, Point start)
    {
        line.X = start.X; line.Y = start.Y;
        line.GridColumn = line.GridRow = null;
        line.LineCellWidth = line.LineCellHeight = 0;
        if (CoordinateGrid(Parent(line)) is { } grid)
        {
            line.GridColumn = start.X / grid.CellSize + .5;
            line.GridRow = start.Y / grid.CellHeight + .5;
            line.X = line.Y = 0;
            line.LineCellWidth = grid.CellSize; line.LineCellHeight = grid.CellHeight;
        }
    }
    public void SetLinePoint(SceneObject line, LineHandle handle, Point point, bool displayUnits = true)
    {
        if (displayUnits && UsesGridCoordinates(line) && CoordinateGrid(Parent(line)) is { } grid)
            point = new((point.X - .5) * grid.CellSize, (point.Y - .5) * grid.CellHeight);
        var start = LineStart(line);
        if (handle == LineHandle.Start)
        {
            line.EndX += start.X - point.X; line.EndY += start.Y - point.Y;
            line.ControlX += start.X - point.X; line.ControlY += start.Y - point.Y;
            SetLineStart(line, point);
        }
        else if (handle == LineHandle.End) { line.EndX = point.X - start.X; line.EndY = point.Y - start.Y; }
        else { line.ControlX = point.X - start.X; line.ControlY = point.Y - start.Y; }
    }
    private static void ClearLinePlacement(SceneObject line)
    {
        line.PointLine = true; line.UseParentLayout = false; line.Angle = 0; line.PreservedTransform = null;
        line.PlacementOffsetX = line.PlacementOffsetY = 0;
        line.LayoutSizing = "固定"; line.LayoutAnchor = "左上"; line.LayoutRegion = "親の外側";
    }
    // Legacy placement is flattened into points. Nonuniform legacy stroke scaling
    // is intentionally not retained; new operations must remain representable.
    public void NormalizeLine(SceneObject line, Matrix? placement = null, bool legacy = true)
    {
        if (line.Kind != ObjectKind.Line || line.PointLine && placement is null) return;
        var matrix = placement ?? EffectiveLocalMatrix(line);
        double sx = new Vector(matrix.M11, matrix.M12).Length, sy = new Vector(matrix.M21, matrix.M22).Length;
        double dot = matrix.M11 * matrix.M21 + matrix.M12 * matrix.M22;
        bool uniform = Math.Abs(sx - sy) < 1e-7 && Math.Abs(dot) < 1e-7;
        bool editableStroke = line.Thickness * sx <= 64 && line.ArrowSize * sx is >= 1 and <= 200;
        if (!legacy && (!uniform || !editableStroke))
            throw new InvalidOperationException("線幅や矢印の形を保つにはフレームが必要です。ラインをフレームにまとめてから操作してください。");
        var end = matrix.Transform(new Vector(line.EndX, line.EndY));
        var control = matrix.Transform(new Vector(line.ControlX, line.ControlY));
        line.EndX = end.X; line.EndY = end.Y; line.ControlX = control.X; line.ControlY = control.Y;
        if (uniform && editableStroke) { line.Thickness *= sx; line.ArrowSize *= sx; }
        ClearLinePlacement(line);
        SetLineStart(line, new(matrix.OffsetX, matrix.OffsetY));
    }
    public static void ScaleLinePoints(SceneObject line, double x, double y)
    {
        line.EndX *= x; line.EndY *= y; line.ControlX *= x; line.ControlY *= y;
    }
    private void ReparentPointLine(SceneObject line, SceneObject target)
    {
        if (!Editable(line) || !Editable(target)) throw new InvalidOperationException("ロックされています。");
        var oldGrid = UsesGridCoordinates(line) ? CoordinateGrid(Parent(line)) : null;
        var newGrid = CoordinateGrid(target);
        var local = World(line); var inverse = World(target); inverse.Invert(); local.Append(inverse);
        Parent(line)!.Children.Remove(line); target.Children.Insert(0, line);
        if (oldGrid is not null && newGrid is not null)
            AdoptGridCoordinates(line);
        else NormalizeLine(line, local, false);
        if (target.IsStateSelector) AssignNewState(target, line);
    }
}
