using System.Windows;
using System.Windows.Media;

namespace GridSketch;

public enum LineHandle { Start, End, Control }

public sealed class LineHandleDrag
{
    private readonly SceneObject node, source;
    private readonly SceneDocument document;
    private readonly Matrix parentWorld;
    private readonly Point start, end, control;
    public LineHandle Handle { get; }
    public LineHandleDrag(SceneDocument document, SceneObject node, LineHandle handle)
    {
        this.node = node; this.document = document; Handle = handle;
        document.NormalizeLine(node);
        source = SceneDocument.Restore(document.Snapshot()).Find(node.Id)!;
        parentWorld = document.Parent(node) is { } parent ? document.World(parent) : Matrix.Identity;
        start = document.LinePoint(node, LineHandle.Start, false);
        end = document.LinePoint(node, LineHandle.End, false);
        control = document.LinePoint(node, LineHandle.Control, false);
    }
    public void Move(Vector worldDelta, double zoom)
    {
        var inverse = parentWorld; inverse.Invert(); var delta = inverse.Transform(worldDelta);
        var original = Handle == LineHandle.Start ? start : Handle == LineHandle.End ? end : control;
        node.X = source.X; node.Y = source.Y; node.GridColumn = source.GridColumn; node.GridRow = source.GridRow;
        node.EndX = source.EndX; node.EndY = source.EndY; node.ControlX = source.ControlX; node.ControlY = source.ControlY;
        if (worldDelta.LengthSquared == 0) return;
        var point = original + delta;
        if (document.UsesGridCoordinates(node) && document.CoordinateGrid(document.Parent(node)) is { } grid)
            point = new(Math.Round((point.X / grid.CellSize + .5) * 2, MidpointRounding.AwayFromZero) / 2 * grid.CellSize - grid.CellSize / 2,
                Math.Round((point.Y / grid.CellHeight + .5) * 2, MidpointRounding.AwayFromZero) / 2 * grid.CellHeight - grid.CellHeight / 2);
        else
        {
            double Snap(double value, Vector axis)
            {
                double scale = zoom * axis.Length;
                double step = scale <= 1 ? 1 : Math.Pow(2, -Math.Ceiling(Math.Log2(scale)));
                return Math.Round(value / step, MidpointRounding.AwayFromZero) * step;
            }
            point = new(Snap(point.X, new(parentWorld.M11, parentWorld.M12)), Snap(point.Y, new(parentWorld.M21, parentWorld.M22)));
        }
        document.SetLinePoint(node, Handle, point, false);
    }

}

public sealed partial class SceneSurface
{
    public LineHandle? HoveredLineHandle { get; set; }
    private double HandleZoom => Math.Max(0.01, Math.Abs(LayoutTransform.Value.M11));
    private IEnumerable<(LineHandle Handle, Point Point)> HandlePoints()
    {
        if (!ShowSelectionBounds || Selected is not { Kind: ObjectKind.Line } node || Renderer is null
            || !Renderer.Document.Shown(node) || !Renderer.Document.Editable(node)) yield break;
        var matrix = Renderer.Document.World(node);
        yield return (LineHandle.Start, matrix.Transform(new Point()));
        yield return (LineHandle.End, matrix.Transform(new Point(node.EndX, node.EndY)));
        if (node.PathKind != "直線") yield return (LineHandle.Control, matrix.Transform(new Point(node.ControlX, node.ControlY)));
    }
    public LineHandle? LineHandleAt(Point surfacePoint)
    {
        var point = ToWorld(surfacePoint);
        return HandlePoints().Where(h => (h.Point - point).Length <= 9 / HandleZoom)
            .OrderBy(h => (h.Point - point).Length).Select(h => (LineHandle?)h.Handle).FirstOrDefault();
    }
    private void DrawLineHandles(DrawingContext dc)
    {
        var points = HandlePoints().ToList();
        if (points.Count == 0) return;
        double zoom = HandleZoom;
        if (Selected!.PathKind == "曲線" && points.Count == 3)
        {
            var guide = new Pen(Brushes.SlateGray, 1 / zoom) { DashStyle = DashStyles.Dash };
            dc.DrawLine(guide, points[0].Point, points[2].Point); dc.DrawLine(guide, points[2].Point, points[1].Point);
        }
        foreach (var (handle, center) in points)
        {
            bool hovered = HoveredLineHandle == handle; double radius = (hovered ? 6 : 4.5) / zoom;
            var fill = hovered ? Brushes.DodgerBlue : Brushes.White; var pen = new Pen(Brushes.DodgerBlue, 1.5 / zoom);
            if (handle != LineHandle.Control) dc.DrawEllipse(fill, pen, center, radius, radius);
            else if (Selected.PathKind == "折れ線") dc.DrawRectangle(fill, pen, new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2));
            else
            {
                var shape = new StreamGeometry();
                using (var c = shape.Open())
                { c.BeginFigure(center + new Vector(0, -radius), true, true); c.LineTo(center + new Vector(radius, 0), true, false); c.LineTo(center + new Vector(0, radius), true, false); c.LineTo(center + new Vector(-radius, 0), true, false); }
                dc.DrawGeometry(fill, pen, shape);
            }
        }
    }
}
