using System.Windows;
using System.Windows.Media;

namespace GridSketch;

public enum LineHandle { Start, End, Control }

public sealed class LineHandleDrag
{
    private readonly SceneObject node, source;
    private readonly Matrix local, world;
    public LineHandle Handle { get; }
    public LineHandleDrag(SceneDocument document, SceneObject node, LineHandle handle)
    {
        this.node = node; Handle = handle;
        source = SceneDocument.Restore(document.Snapshot()).Find(node.Id)!;
        local = document.EffectiveLocalMatrix(node); world = document.World(node);
    }
    public void Move(Vector worldDelta, double zoom)
    {
        var inverse = world; inverse.Invert(); var delta = inverse.Transform(worldDelta);
        var original = Handle == LineHandle.Start ? new Point() : Handle == LineHandle.End
            ? new Point(source.EndX, source.EndY) : new Point(source.ControlX, source.ControlY);
        double Snap(double value, Vector axis)
        {
            double scale = zoom * axis.Length;
            double step = scale <= 1 ? 1 : Math.Pow(2, -Math.Ceiling(Math.Log2(scale)));
            return Math.Round(value / step, MidpointRounding.AwayFromZero) * step;
        }
        var point = new Point(Snap(original.X + delta.X, new(world.M11, world.M12)), Snap(original.Y + delta.Y, new(world.M21, world.M22)));
        node.EndX = source.EndX; node.EndY = source.EndY; node.ControlX = source.ControlX; node.ControlY = source.ControlY;
        if (point == original)
        {
            node.X = source.X; node.Y = source.Y; node.Angle = source.Angle;
            node.UseParentLayout = source.UseParentLayout; node.PreservedTransform = source.PreservedTransform; return;
        }
        // Freeze the current placement while editing geometry, so automatic fitting
        // does not move the untouched points as the line's bounds change.
        node.UseParentLayout = false; node.Angle = 0;
        node.PreservedTransform = new() { M11 = local.M11, M12 = local.M12, M21 = local.M21, M22 = local.M22 };
        node.X = local.OffsetX; node.Y = local.OffsetY;
        if (Handle == LineHandle.Start)
        {
            var start = local.Transform(point); node.X = start.X; node.Y = start.Y;
            node.EndX -= point.X; node.EndY -= point.Y; node.ControlX -= point.X; node.ControlY -= point.Y;
        }
        else if (Handle == LineHandle.End) { node.EndX = point.X; node.EndY = point.Y; }
        else { node.ControlX = point.X; node.ControlY = point.Y; }
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
