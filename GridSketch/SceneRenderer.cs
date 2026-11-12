using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GridSketch;

public sealed class SceneRenderer
{
    public SceneDocument Document { get; }
    public DrawingGroup Drawing { get; } = new();
    public Rect ContentBounds { get; private set; } = Rect.Empty;
    public Rect OutputBounds { get; private set; }
    public Dictionary<string, Rect> Bounds { get; } = [];
    public SceneRenderer(SceneDocument document)
    {
        Document = document;
        using (var dc = Drawing.Open()) foreach (var node in document.Canvas.Children.AsEnumerable().Reverse()) DrawNode(dc, node, null);
        var root = document.Canvas;
        if (root.AutoSize)
        {
            var rect = ContentBounds;
            if (rect.IsEmpty) rect = new Rect(0, 0, 800, 600);
            rect.Inflate(root.Padding, root.Padding);
            OutputBounds = new Rect(Math.Floor(rect.Left), Math.Floor(rect.Top), Math.Max(1, Math.Ceiling(rect.Right) - Math.Floor(rect.Left)), Math.Max(1, Math.Ceiling(rect.Bottom) - Math.Floor(rect.Top)));
        }
        else OutputBounds = new Rect(root.X, root.Y, root.CanvasPixelWidth, root.CanvasPixelHeight);
        Bounds[root.Id] = OutputBounds;
    }
    public static Brush Brush(string color) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    private static Pen Pen(SceneObject node) => new(Brush(node.Color), node.Thickness)
    {
        StartLineCap = BorderCap(node),
        EndLineCap = BorderCap(node),
        DashStyle = node.LineStyle switch { "破線" => DashStyles.Dash, "点線" => DashStyles.Dot, _ => DashStyles.Solid }
    };
    private static PenLineCap BorderCap(SceneObject node) => node.Kind != ObjectKind.Border ? PenLineCap.Square : node.EffectiveBorderEndCap switch
    {
        "中央まで" => PenLineCap.Flat,
        "丸" => PenLineCap.Round,
        _ => PenLineCap.Square
    };
    public static Transform LocalTransform(SceneObject node)
    {
        return new MatrixTransform(SceneDocument.LocalMatrix(node));
    }
    private void DrawNode(DrawingContext parent, SceneObject node, SceneObject? grid, bool recordBounds = true, bool applyLayout = true)
    {
        if (!node.Visible) return;
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            if (node.Kind == ObjectKind.Grid) grid = node;
            if (!node.IsStateSelector)
                foreach (var child in node.Children.AsEnumerable().Reverse()) DrawNode(dc, child, grid, recordBounds, applyLayout);
            var pen = node.Thickness > 0 ? Pen(node) : null;
            switch (node.Kind)
            {
                case ObjectKind.EdgeSelection when grid is not null:
                    var edgeTemplates = node.Children.Where(c => c.Visible).ToDictionary(c => c.Id, c => ObjectTemplate(c));
                    foreach (var placement in node.StateEdges)
                    {
                        var assigned = node.States.Find(s => s.Id == placement.StateId)?.ObjectId;
                        if (assigned is null || !edgeTemplates.TryGetValue(assigned, out var picture)) continue;
                        var child = node.Children.Single(c => c.Id == assigned);
                        var center = SceneDocument.EdgeCenter(grid, placement.Edge);
                        dc.PushTransform(new TranslateTransform(center.X, center.Y));
                        double angle = placement.Edge.Vertical ? child.VerticalRotation switch { "右90°" => 90, "左90°" => -90, _ => 0 } : 0;
                        dc.PushTransform(new RotateTransform(angle));
                        dc.PushTransform(new TranslateTransform(-grid.CellSize / 2, -grid.CellHeight / 2));
                        dc.PushClip(new RectangleGeometry(new Rect(0, 0, grid.CellSize, grid.CellHeight)));
                        dc.DrawDrawing(picture); dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
                    }
                    break;
                case ObjectKind.CellSelection when grid is not null:
                    var templates = node.Children.Where(c => c.Visible).ToDictionary(c => c.Id, c => node.UseObjectLayout ? ObjectTemplate(c) : node.UseCellLayout ? CellTemplate(c, grid) : Template(c));
                    var states = node.States.ToDictionary(s => s.Id);
                    foreach (var cell in node.StateCells)
                    {
                        if (!states.TryGetValue(cell.StateId, out var state) || state.ObjectId is null || !templates.TryGetValue(state.ObjectId, out var template)) continue;
                        if (node.UseCellLayout || node.UseObjectLayout)
                        {
                            var fullCell = SceneDocument.CellRect(grid, new(cell.Column, cell.Row), false);
                            dc.PushClip(new RectangleGeometry(fullCell));
                            dc.PushTransform(new TranslateTransform(fullCell.X, fullCell.Y));
                            dc.DrawDrawing(template); dc.Pop(); dc.Pop();
                            continue;
                        }
                        var bounds = template.Bounds;
                        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) continue;
                        var box = SceneDocument.CellRect(grid, new(cell.Column, cell.Row), true);
                        double scale = Math.Min(1, Math.Min(box.Width / bounds.Width, box.Height / bounds.Height));
                        dc.PushClip(new RectangleGeometry(box));
                        dc.PushTransform(new TranslateTransform(box.X + (box.Width - bounds.Width * scale) / 2, box.Y + (box.Height - bounds.Height * scale) / 2));
                        dc.PushTransform(new ScaleTransform(scale, scale)); dc.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
                        dc.DrawDrawing(template); dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
                    }
                    break;
                case ObjectKind.Border when grid is not null:
                    if (pen is not null) DrawBorders(dc, node, grid, pen);
                    break;
                case ObjectKind.Fill when grid is not null:
                    foreach (var cell in node.FilledCells) dc.DrawRectangle(Brush(node.Color), null, SceneDocument.CellRect(grid, cell, node.UseCellMargin));
                    break;
                case ObjectKind.CellFrame when grid is not null:
                    dc.DrawDrawing(RegionFrameDrawing.Drawing(node, grid));
                    break;
                case ObjectKind.GridText when grid is not null:
                    foreach (var glyph in node.Glyphs)
                    {
                        var box = SceneDocument.CellRect(grid, new(glyph.Column, glyph.Row), node.UseCellMargin);
                        if (!glyph.InCellText) dc.PushClip(new RectangleGeometry(SceneDocument.CellRect(grid, new(glyph.Column, glyph.Row), false)));
                        var text = StyledText(node, glyph.Text, glyph.Font ?? node.Font, glyph.Size ?? node.FontSize);
                        var bounds = text.Bounds;
                        if (!bounds.IsEmpty)
                        {
                            double spacing = node.Alignment == "中央" ? 1 : 2;
                            double insetX = Math.Min(spacing, box.Width / 4), insetY = Math.Min(spacing, box.Height / 4);
                            double scale = Math.Min(1, Math.Min((box.Width - insetX * 2) / Math.Max(1, bounds.Width), (box.Height - insetY * 2) / Math.Max(1, bounds.Height)));
                            if (glyph.InCellText) scale = 1;
                            double ax = node.Alignment.Contains('左') ? 0 : node.Alignment.Contains('右') ? 1 : 0.5;
                            double ay = node.Alignment.Contains('上') ? 0 : node.Alignment.Contains('下') ? 1 : 0.5;
                            var x = box.X + insetX + (box.Width - insetX * 2 - bounds.Width * scale) * ax + node.PlacementOffsetX;
                            var y = box.Y + insetY + (box.Height - insetY * 2 - bounds.Height * scale) * ay + node.PlacementOffsetY;
                            dc.PushTransform(new TranslateTransform(x, y)); dc.PushTransform(new ScaleTransform(scale, scale)); dc.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
                            dc.DrawDrawing(text); dc.Pop(); dc.Pop(); dc.Pop();
                        }
                        if (!glyph.InCellText) dc.Pop();
                    }
                    break;
                case ObjectKind.Image when node.ImageData is not null:
                    var bitmap = DocumentImage(node.ImageData);
                    var imageBox = ImagePlacementRect(node, grid);
                    dc.DrawImage(bitmap, imageBox);
                    break;
                case ObjectKind.Text:
                    dc.DrawDrawing(StyledText(node, node.Text, node.Font, node.FontSize)); break;
                case ObjectKind.Shape:
                    var rect = new Rect(0, 0, node.Width, node.Height);
                    if (node.Shape == "楕円") dc.DrawEllipse(Brush(node.FillColor), pen, new Point(node.Width / 2, node.Height / 2), node.Width / 2, node.Height / 2);
                    else dc.DrawRoundedRectangle(Brush(node.FillColor), pen, rect, node.Shape == "角丸" ? 12 : 0, node.Shape == "角丸" ? 12 : 0);
                    break;
                case ObjectKind.Line:
                    var start = new Point(); var end = new Point(node.EndX, node.EndY); var control = new Point(node.ControlX, node.ControlY);
                    var geometry = new StreamGeometry();
                    using (var context = geometry.Open())
                    {
                        context.BeginFigure(start, false, false);
                        if (node.PathKind == "曲線") context.QuadraticBezierTo(control, end, true, false);
                        else { if (node.PathKind == "折れ線") context.LineTo(control, true, false); context.LineTo(end, true, false); }
                    }
                    // Cut the stroked shaft at the base of each filled arrowhead, including
                    // square caps and dash caps. Union the head so translucent colors do not overlap.
                    Geometry shaft = pen is null ? Geometry.Empty : geometry.GetWidenedPathGeometry(pen, 0.02, ToleranceType.Absolute);
                    var startDirection = node.PathKind == "直線" ? start - end : start - control;
                    var endDirection = node.PathKind == "直線" ? end - start : end - control;
                    if (startDirection.Length < 0.001) startDirection = start - end;
                    if (endDirection.Length < 0.001) endDirection = end - start;
                    var heads = new List<Geometry>();
                    void TrimHead(Point tip, Vector direction, string type)
                    {
                        if (type != "三角" || direction.Length < 0.001) return;
                        direction.Normalize();
                        var normal = new Vector(-direction.Y, direction.X);
                        var basis = tip - direction * node.ArrowSize;
                        // Restrict removal to the arrowhead's neighborhood so a bent path
                        // elsewhere beyond the endpoint plane remains intact.
                        double reach = node.ArrowSize + node.Thickness + 1;
                        var cut = Polygon(basis + normal * reach, basis - normal * reach,
                            tip + direction * reach - normal * reach, tip + direction * reach + normal * reach);
                        shaft = Geometry.Combine(shaft, cut, GeometryCombineMode.Exclude, null, 0.02, ToleranceType.Absolute);
                        heads.Add(Polygon(tip, basis + normal * node.ArrowSize * 0.45, basis - normal * node.ArrowSize * 0.45));
                    }
                    TrimHead(start, startDirection, node.StartArrow);
                    TrimHead(end, endDirection, node.EndArrow);
                    if (heads.Count == 0) dc.DrawGeometry(null, pen, geometry);
                    else
                    {
                        foreach (var head in heads) shaft = Geometry.Combine(shaft, head, GeometryCombineMode.Union, null, 0.02, ToleranceType.Absolute);
                        dc.DrawGeometry(Brush(node.Color), null, shaft);
                    }
                    if (node.StartArrow == "開いた矢印") Arrow(dc, node, start, startDirection, node.StartArrow);
                    if (node.EndArrow == "開いた矢印") Arrow(dc, node, end, endDirection, node.EndArrow);
                    break;
            }
        }
        var localBounds = drawing.Bounds;
        if (applyLayout && node.Kind == ObjectKind.Group && node.HasLayoutFrame && !node.OrganizeOnly) localBounds.Union(SceneDocument.OwnLayoutRect(node));
        if (node.Kind == ObjectKind.Grid)
        {
            // Reserve the full grid and configured stroke overhang even when individual edges are empty.
            var reservation = new Rect(0, 0, node.Columns * node.CellSize, node.Rows * node.CellHeight);
            var overhang = SceneDocument.GridMembers(node).Where(c => Document.Shown(c) && c.Kind == ObjectKind.Border).Select(c => c.Thickness / 2).DefaultIfEmpty(0).Max();
            reservation.Inflate(overhang, overhang); localBounds.Union(reservation);
        }
        if (recordBounds && !localBounds.IsEmpty)
        {
            var world = new MatrixTransform(Document.World(node)).TransformBounds(localBounds);
            Bounds[node.Id] = world;
            var content = ContentBounds; content.Union(world); ContentBounds = content;
        }
        drawing.Transform = applyLayout ? new MatrixTransform(Document.EffectiveLocalMatrix(node)) : LocalTransform(node); parent.DrawDrawing(drawing);
    }
    public DrawingGroup ObjectTemplate(SceneObject node)
    {
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open()) DrawNode(dc, node, null, false);
        return drawing;
    }
    public DrawingGroup Template(SceneObject node)
    {
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open()) DrawNode(dc, node, null, false, false);
        return drawing;
    }
    public DrawingGroup CellTemplate(SceneObject node, SceneObject grid)
    {
        var result = new DrawingGroup();
        using var dc = result.Open();
        if (!node.Visible) return result;
        if (node.Kind == ObjectKind.Group)
        {
            // Groups pass both regions through unchanged. Sibling bounds never determine scale.
            dc.PushTransform(LocalTransform(node));
            foreach (var child in node.Children.AsEnumerable().Reverse()) dc.DrawDrawing(CellTemplate(child, grid));
            dc.Pop();
            return result;
        }
        var drawing = Template(node);
        var bounds = drawing.Bounds;
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return result;
        var region = SceneDocument.CellRect(grid, new(0, 0), node.TemplateRegion == "余白の内側");
        double sx = 1, sy = 1;
        if (node.TemplateSizing == "収める") sx = sy = Math.Min(region.Width / bounds.Width, region.Height / bounds.Height);
        else if (node.TemplateSizing == "親いっぱい") { sx = region.Width / bounds.Width; sy = region.Height / bounds.Height; }
        double ax = node.TemplateAnchor.Contains('左') ? 0 : node.TemplateAnchor.Contains('右') ? 1 : 0.5;
        double ay = node.TemplateAnchor.Contains('上') ? 0 : node.TemplateAnchor.Contains('下') ? 1 : 0.5;
        double x = region.X + (region.Width - bounds.Width * sx) * ax + node.X;
        double y = region.Y + (region.Height - bounds.Height * sy) * ay + node.Y;
        dc.PushClip(new RectangleGeometry(region));
        dc.PushTransform(new TranslateTransform(x, y));
        dc.PushTransform(new ScaleTransform(sx, sy));
        dc.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
        dc.DrawDrawing(drawing); dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
        return result;
    }
    private static BitmapSource DocumentImage(byte[] bytes) => GridSketch.Document.DecodeImage(bytes);
    private static void DrawBorders(DrawingContext dc, SceneObject node, SceneObject grid, Pen pen)
    {
        double width = grid.CellSize, height = grid.CellHeight;
        double offset = node.ConnectCellCenters ? 0.5 : 0;
        dc.PushTransform(new TranslateTransform(offset * width, offset * height));
        var translucent = pen.Brush is SolidColorBrush brush && (brush.Color.A < 255 || brush.Opacity < 1);
        var coverage = translucent ? new GeometryGroup { FillRule = FillRule.Nonzero } : null;
        void Run(bool vertical, int axis, int start, int end)
        {
            var from = vertical ? new Point(axis * width, start * height) : new Point(start * width, axis * height);
            var to = vertical ? new Point(axis * width, end * height) : new Point(end * width, axis * height);
            if (coverage is null) dc.DrawLine(pen, from, to);
            else coverage.Children.Add(new LineGeometry(from, to).GetWidenedPathGeometry(pen, 0.02, ToleranceType.Absolute));
        }
        var edges = SceneDocument.BorderEdges(node, grid).ToList();
        foreach (var group in edges.Where(e => e.Diagonal != 0).GroupBy(e => (e.Diagonal, Axis: e.Diagonal == 1 ? e.Row - e.Column : e.Row + e.Column)))
        {
            var ordered = group.OrderBy(e => e.Column).ToList();
            void DrawDiagonal(GridEdge first, GridEdge last)
            {
                var a = first.Start;
                var b = last.End;
                var from = new Point(a.X * width, a.Y * height);
                var to = new Point(b.X * width, b.Y * height);
                if (coverage is null) dc.DrawLine(pen, from, to);
                else coverage.Children.Add(new LineGeometry(from, to).GetWidenedPathGeometry(pen, 0.02, ToleranceType.Absolute));
            }
            var first = ordered[0]; var last = first;
            foreach (var edge in ordered.Skip(1))
            {
                if (!node.JoinSegments || edge.Column != last.Column + 1) { DrawDiagonal(first, last); first = edge; }
                last = edge;
            }
            DrawDiagonal(first, last);
        }
        foreach (var group in edges.Where(e => e.Diagonal == 0).GroupBy(e => (e.Vertical, Axis: e.Vertical ? e.Column : e.Row)))
        {
            var indices = group.Select(e => e.Vertical ? e.Row : e.Column).OrderBy(i => i).ToList();
            int start = indices[0], end = start + 1;
            foreach (var index in indices.Skip(1))
            {
                if (node.JoinSegments && index == end) end++;
                else { Run(group.Key.Vertical, group.Key.Axis, start, end); start = index; end = index + 1; }
            }
            Run(group.Key.Vertical, group.Key.Axis, start, end);
        }
        // Fill all stroke outlines once so intersections share the layer's alpha in PNG and vector PDF.
        if (coverage is not null) dc.DrawGeometry(pen.Brush, null, coverage);
        dc.Pop();
    }
    private static Geometry TextGeometry(string text, string font, double size) => new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(font), size, Brushes.Black, 1).BuildGeometry(new Point());
    internal static DrawingGroup StyledText(SceneObject node, string value, string font, double size)
    {
        var geometry = TextGeometry(value, font, size);
        var drawing = new DrawingGroup();
        using var dc = drawing.Open();
        if (node.Style.OutlineEnabled && node.Style.OutlineWidth > 0 && !geometry.Bounds.IsEmpty)
        {
            // Subtract the glyph interior so translucent text never reveals an underlying stroke.
            var pen = new Pen(Brushes.Black, node.Style.OutlineWidth * 2) { LineJoin = PenLineJoin.Round };
            var widened = geometry.GetWidenedPathGeometry(pen, 0.02, ToleranceType.Absolute);
            var outline = Geometry.Combine(widened, geometry, GeometryCombineMode.Exclude, null, 0.02, ToleranceType.Absolute);
            dc.DrawGeometry(Brush(node.Style.OutlineColor), null, outline);
        }
        dc.DrawGeometry(Brush(node.Color), null, geometry);
        return drawing;
    }
    private static StreamGeometry Polygon(params Point[] points)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(points[0], true, true);
        foreach (var point in points.Skip(1)) context.LineTo(point, true, false);
        return geometry;
    }
    private static void Arrow(DrawingContext dc, SceneObject node, Point tip, Vector direction, string type)
    {
        if (type == "なし" || direction.Length < 0.001) return;
        direction.Normalize(); var normal = new Vector(-direction.Y, direction.X);
        var a = tip - direction * node.ArrowSize + normal * node.ArrowSize * 0.45;
        var b = tip - direction * node.ArrowSize - normal * node.ArrowSize * 0.45;
        if (type == "開いた矢印") { dc.DrawLine(Pen(node), a, tip); dc.DrawLine(Pen(node), tip, b); }
        else
        {
            var triangle = new StreamGeometry(); using (var c = triangle.Open()) { c.BeginFigure(tip, true, true); c.LineTo(a, true, false); c.LineTo(b, true, false); }
            dc.DrawGeometry(Brush(node.Color), null, triangle);
        }
    }
    public static Rect ImagePlacementRect(SceneObject node, SceneObject? grid)
    {
        if (node.ImageData is null) return new Rect(0, 0, node.Width, node.Height);
        var bitmap = DocumentImage(node.ImageData);
        if (node.SnapToCell && grid is not null)
        {
            var available = SceneDocument.CellRect(grid, new(node.Column, node.Row), true);
            var ratio = Math.Min(1, Math.Min(available.Width / bitmap.PixelWidth, available.Height / bitmap.PixelHeight));
            return new Rect(node.Column * grid.CellSize + (grid.CellSize - bitmap.PixelWidth * ratio) / 2,
                node.Row * grid.CellHeight + (grid.CellHeight - bitmap.PixelHeight * ratio) / 2, bitmap.PixelWidth * ratio, bitmap.PixelHeight * ratio);
        }
        var scale = Math.Min(node.Width / bitmap.PixelWidth, node.Height / bitmap.PixelHeight);
        return new Rect((node.Width - bitmap.PixelWidth * scale) / 2, (node.Height - bitmap.PixelHeight * scale) / 2,
            bitmap.PixelWidth * scale, bitmap.PixelHeight * scale);
    }
    public BitmapSource Export(int scale = 1)
    {
        if (scale is < 1 or > 4) throw new InvalidOperationException("出力倍率は1～4です。");
        double requestedWidth = Math.Ceiling(OutputBounds.Width * scale), requestedHeight = Math.Ceiling(OutputBounds.Height * scale);
        if (requestedWidth * requestedHeight > 40_000_000) throw new InvalidOperationException("PNGは4000万画素以下にしてください。");
        int width = (int)requestedWidth, height = (int)requestedHeight;
        if (width < 1 || height < 1 || (long)width * height > 40_000_000) throw new InvalidOperationException("PNGは4000万画素以下にしてください。");
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(scale, scale)); dc.PushTransform(new TranslateTransform(-OutputBounds.X, -OutputBounds.Y));
            dc.PushClip(new RectangleGeometry(OutputBounds)); dc.DrawRectangle(Brush(Document.Canvas.Background), null, OutputBounds); dc.DrawDrawing(Drawing);
        }
        var rendered = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); rendered.Render(visual);
        var pixels = new byte[width * height * 4]; rendered.CopyPixels(pixels, width * 4, 0);
        // Pixel dimensions already reflect the canvas unit and DPI; render at those exact dimensions.
        return BitmapSource.Create(width, height, Document.Canvas.Dpi, Document.Canvas.Dpi, PixelFormats.Pbgra32, null, pixels, width * 4);
    }
}

public sealed partial class SceneSurface : FrameworkElement
{
    public (SceneObject Grid, Rect Bounds)? GridStructurePreview { get; set; }
    public bool ShowSelectionBounds { get; set; }
    private double minimumWorkspaceWidth = 2400, minimumWorkspaceHeight = 1800;
    public const double Origin = 1024;
    public double OriginX { get; private set; } = Origin;
    public double OriginY { get; private set; } = Origin;
    public SceneRenderer? Renderer { get; private set; }
    public SceneObject? Selected { get; set; }
    public CellPosition? ActiveCell { get; set; }
    public bool TextVertical { get; set; }
    public bool TextInCell { get; set; }
    public CellPosition? RangeEnd { get; set; }
    public GridEdge? HoveredEdge { get; set; }
    public List<Glyph> Preview { get; set; } = [];
    public void Refresh(SceneDocument document)
    {
        Renderer = new SceneRenderer(document);
        var extents = Renderer.OutputBounds; extents.Union(Renderer.ContentBounds);
        OriginX = Math.Max(OriginX, Math.Ceiling(-extents.Left) + 128);
        OriginY = Math.Max(OriginY, Math.Ceiling(-extents.Top) + 128);
        Width = Math.Max(minimumWorkspaceWidth, extents.Right + OriginX + 300);
        Height = Math.Max(minimumWorkspaceHeight, extents.Bottom + OriginY + 300);
        InvalidateVisual();
    }
    public Point ToWorld(Point p) => new(p.X - OriginX, p.Y - OriginY);
    public void EnsureZoomSpace(Point center, Size visibleSize)
    {
        // Keep enough workspace around the anchor so scroll limits do not shift it at low zoom.
        double oldX = OriginX, oldY = OriginY;
        OriginX = Math.Max(OriginX, visibleSize.Width / 2 + 16 - center.X);
        OriginY = Math.Max(OriginY, visibleSize.Height / 2 + 16 - center.Y);
        minimumWorkspaceWidth = Math.Max(Width + OriginX - oldX, center.X + OriginX + visibleSize.Width / 2 + 16);
        minimumWorkspaceHeight = Math.Max(Height + OriginY - oldY, center.Y + OriginY + visibleSize.Height / 2 + 16);
        Width = minimumWorkspaceWidth; Height = minimumWorkspaceHeight;
        InvalidateVisual();
    }
    public Point ToLocal(SceneObject node, Point surfacePoint)
    {
        var inverse = Renderer!.Document.World(node); inverse.Invert(); return inverse.Transform(ToWorld(surfacePoint));
    }
    public Point EditLocal(SceneObject grid, Point surfacePoint) => ToLocal(
        Selected is { IsGridContent: true } node && Renderer!.Document.GridFor(node) == grid ? node : grid, surfacePoint);
    public CellPosition? CellAt(SceneObject grid, Point surfacePoint)
    {
        var p = EditLocal(grid, surfacePoint); int c = (int)Math.Floor(p.X / grid.CellSize), r = (int)Math.Floor(p.Y / grid.CellHeight);
        return c >= 0 && r >= 0 && c < grid.Columns && r < grid.Rows ? new(c, r) : null;
    }
    public GridEdge? EdgeAt(SceneObject grid, Point surfacePoint, double zoom)
    {
        var border = Selected is { Kind: ObjectKind.Border } selected ? selected : new SceneObject();
        var (columns, rows) = SceneDocument.BorderDimensions(border, grid);
        var p = EditLocal(grid, surfacePoint);
        if (border.ConnectCellCenters) return SceneDocument.CenterEdgeAt(border, grid, p, zoom);
        double width = grid.CellSize, height = grid.CellHeight;
        double toleranceX = Math.Min(width * 0.22, 7 / zoom), toleranceY = Math.Min(height * 0.22, 7 / zoom);
        if (border.ConnectCellCenters) p.Offset(-width / 2, -height / 2);
        if (p.X < -toleranceX || p.Y < -toleranceY || p.X > columns * width + toleranceX || p.Y > rows * height + toleranceY) return null;
        int c = (int)Math.Round(p.X / width), r = (int)Math.Round(p.Y / height);
        double dx = Math.Abs(p.X - c * width), dy = Math.Abs(p.Y - r * height);
        var vertical = new GridEdge(c, Math.Clamp((int)Math.Floor(p.Y / height), 0, Math.Max(0, rows - 1)), true);
        var horizontal = new GridEdge(Math.Clamp((int)Math.Floor(p.X / width), 0, Math.Max(0, columns - 1)), r, false);
        bool v = vertical.IsInside(columns, rows) && dx <= toleranceX, h = horizontal.IsInside(columns, rows) && dy <= toleranceY;
        if (Selected is { Kind: ObjectKind.EdgeSelection } selector)
        { v &= SceneDocument.EdgeSelectable(selector, vertical); h &= SceneDocument.EdgeSelectable(selector, horizontal); }
        if (v && (!h || dx <= dy)) return vertical;
        return h ? horizontal : null;
    }
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.LightSlateGray, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (Renderer is null) return;
        dc.PushTransform(new TranslateTransform(OriginX, OriginY));
        var bounds = Renderer.OutputBounds;
        // A checker pattern makes transparent output distinguishable from white output.
        var checker = new DrawingGroup(); using (var c = checker.Open())
        {
            c.DrawRectangle(Brushes.White, null, new Rect(0, 0, 16, 16));
            c.DrawRectangle(Brushes.LightGray, null, new Rect(0, 0, 8, 8)); c.DrawRectangle(Brushes.LightGray, null, new Rect(8, 8, 8, 8));
        }
        var brush = new DrawingBrush(checker) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 16, 16), ViewportUnits = BrushMappingMode.Absolute };
        dc.DrawRectangle(brush, null, bounds); dc.DrawRectangle(SceneRenderer.Brush(Renderer.Document.Canvas.Background), null, bounds);
        dc.DrawDrawing(Renderer.Drawing); dc.DrawRectangle(null, new Pen(Brushes.DimGray, 1), bounds);
        if (Selected is { } selected && Renderer.Document.Shown(selected))
        {
            if (ShowSelectionBounds && Renderer.Bounds.TryGetValue(selected.Id, out var selectedBounds))
            {
                if (selected.Kind == ObjectKind.Group && selected.HasLayoutFrame && !selected.OrganizeOnly)
                {
                    dc.PushTransform(new MatrixTransform(Renderer.Document.World(selected)));
                    dc.DrawRectangle(null, new Pen(Brushes.DodgerBlue, 1) { DashStyle = DashStyles.Dash }, SceneDocument.OwnLayoutRect(selected)); dc.Pop();
                }
                else dc.DrawRectangle(null, new Pen(Brushes.DodgerBlue, 1) { DashStyle = DashStyles.Dash }, selectedBounds);
                if (!selected.IsGridContent && Renderer.Document.SelectionOwner(selected) is null && Renderer.Document.Parent(selected) is { } parent)
                {
                    var regions = Renderer.Document.ParentRegions(selected);
                    dc.PushTransform(new MatrixTransform(Renderer.Document.World(parent)));
                    if (regions.Outer is { } outer) dc.DrawRectangle(null, new Pen(Brushes.Gray, 1) { DashStyle = DashStyles.Dot }, outer);
                    if (regions.Inner is { } inner) dc.DrawRectangle(null, new Pen(Brushes.SeaGreen, 1) { DashStyle = DashStyles.Dot }, inner);
                    dc.Pop();
                }
            }
            var grid = Renderer.Document.GridFor(selected);
            if (grid is not null)
            {
                dc.PushTransform(new MatrixTransform(Renderer.Document.World(selected.IsGridContent ? selected : grid)));
                if (ActiveCell is { } cell)
                {
                    var end = RangeEnd ?? cell;
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(25, 30, 100, 220)), new Pen(Brushes.DodgerBlue, 1), new Rect(Math.Min(cell.Column, end.Column) * grid.CellSize, Math.Min(cell.Row, end.Row) * grid.CellHeight, (Math.Abs(cell.Column - end.Column) + 1) * grid.CellSize, (Math.Abs(cell.Row - end.Row) + 1) * grid.CellHeight));
                    if (selected.Kind == ObjectKind.GridText && end == cell)
                    {
                        var p = new Point((cell.Column + 0.76) * grid.CellSize, (cell.Row + 0.76) * grid.CellHeight);
                        var d = TextVertical ? new Vector(0, grid.CellHeight * 0.17) : new Vector(grid.CellSize * 0.17, 0);
                        var side = new Vector(-d.Y, d.X) * 0.4;
                        var pen = new Pen(Brushes.DodgerBlue, 2);
                        if (TextInCell) dc.DrawRectangle(null, pen, new Rect(p.X - grid.CellSize * 0.09, p.Y - grid.CellHeight * 0.09, grid.CellSize * 0.18, grid.CellHeight * 0.18));
                        else
                        {
                        dc.DrawLine(pen, p - d, p + d);
                        dc.DrawLine(pen, p + d, p + d * 0.3 + side);
                        dc.DrawLine(pen, p + d, p + d * 0.3 - side);
                        }
                    }
                }
                if (HoveredEdge is { } edge)
                {
                    double offset = selected.ConnectCellCenters ? 0.5 : 0;
                    var p = new Point((edge.Start.X + offset) * grid.CellSize, (edge.Start.Y + offset) * grid.CellHeight);
                    dc.DrawLine(new Pen(Brushes.DodgerBlue, 3), p, new Point((edge.End.X + offset) * grid.CellSize, (edge.End.Y + offset) * grid.CellHeight));
                }
                foreach (var glyph in Preview)
                {
                    var rect = SceneDocument.CellRect(grid, new(glyph.Column, glyph.Row), false);
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(100, 255, 220, 70)), new Pen(Brushes.DarkOrange, 1), rect);
                    var text = new FormattedText(glyph.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(selected.Font), Math.Min(selected.FontSize, Math.Min(grid.CellSize, grid.CellHeight) * 0.7), Brushes.DarkOrange, 1);
                    dc.DrawText(text, new Point(rect.X + (rect.Width - text.Width) / 2, rect.Y + (rect.Height - text.Height) / 2));
                }
                dc.Pop();
            }
        }
        if (GridStructurePreview is { } preview)
        {
            dc.PushTransform(new MatrixTransform(Renderer.Document.World(preview.Grid)));
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(70, 220, 60, 40)), null, preview.Bounds);
            dc.Pop();
        }
        DrawLineHandles(dc);
        dc.Pop();
    }
}
