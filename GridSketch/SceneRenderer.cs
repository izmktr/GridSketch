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
        else OutputBounds = new Rect(root.X, root.Y, root.Width, root.Height);
        Bounds[root.Id] = OutputBounds;
    }
    public static Brush Brush(string color) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    private static Pen Pen(SceneObject node) => new(Brush(node.Color), node.Thickness)
    {
        StartLineCap = node.Kind == ObjectKind.Border && !node.SquareCaps ? PenLineCap.Flat : PenLineCap.Square,
        EndLineCap = node.Kind == ObjectKind.Border && !node.SquareCaps ? PenLineCap.Flat : PenLineCap.Square,
        DashStyle = node.LineStyle switch { "破線" => DashStyles.Dash, "点線" => DashStyles.Dot, _ => DashStyles.Solid }
    };
    public static Transform LocalTransform(SceneObject node)
    {
        var m = Matrix.Identity; m.Rotate(node.Angle); m.Translate(node.X, node.Y); return new MatrixTransform(m);
    }
    private void DrawNode(DrawingContext parent, SceneObject node, SceneObject? grid)
    {
        if (!node.Visible) return;
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            if (node.Kind == ObjectKind.Grid) grid = node;
            foreach (var child in node.Children.AsEnumerable().Reverse()) DrawNode(dc, child, grid);
            var pen = node.Thickness > 0 ? Pen(node) : null;
            switch (node.Kind)
            {
                case ObjectKind.Border when grid is not null:
                    if (pen is not null) DrawBorders(dc, node, grid, pen);
                    break;
                case ObjectKind.Fill when grid is not null:
                    foreach (var cell in node.FilledCells) dc.DrawRectangle(Brush(node.Color), null, new Rect(cell.Column * grid.CellSize, cell.Row * grid.CellSize, grid.CellSize, grid.CellSize));
                    break;
                case ObjectKind.GridText when grid is not null:
                    foreach (var glyph in node.Glyphs)
                    {
                        var box = new Rect(glyph.Column * grid.CellSize, glyph.Row * grid.CellSize, grid.CellSize, grid.CellSize);
                        dc.PushClip(new RectangleGeometry(box));
                        var text = TextGeometry(glyph.Text, glyph.Font ?? node.Font, glyph.Size ?? node.FontSize);
                        var bounds = text.Bounds;
                        if (!bounds.IsEmpty)
                        {
                            double scale = Math.Min(1, Math.Min((grid.CellSize - 2) / Math.Max(1, bounds.Width), (grid.CellSize - 2) / Math.Max(1, bounds.Height)));
                            var x = node.Alignment == "左上" ? box.X + 2 : box.X + (box.Width - bounds.Width * scale) / 2;
                            var y = node.Alignment == "左上" ? box.Y + 2 : box.Y + (box.Height - bounds.Height * scale) / 2;
                            dc.PushTransform(new TranslateTransform(x, y)); dc.PushTransform(new ScaleTransform(scale, scale)); dc.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
                            dc.DrawGeometry(Brush(node.Color), null, text); dc.Pop(); dc.Pop(); dc.Pop();
                        }
                        dc.Pop();
                    }
                    break;
                case ObjectKind.Image when node.ImageData is not null:
                    var bitmap = DocumentImage(node.ImageData);
                    var imageBox = new Rect(0, 0, node.Width, node.Height);
                    if (node.SnapToCell && grid is not null)
                    {
                        var available = grid.CellSize - grid.Margin * 2;
                        var ratio = Math.Min(1, Math.Min(available / bitmap.PixelWidth, available / bitmap.PixelHeight));
                        imageBox = new Rect(node.Column * grid.CellSize + (grid.CellSize - bitmap.PixelWidth * ratio) / 2, node.Row * grid.CellSize + (grid.CellSize - bitmap.PixelHeight * ratio) / 2, bitmap.PixelWidth * ratio, bitmap.PixelHeight * ratio);
                    }
                    else
                    {
                        var ratio = Math.Min(node.Width / bitmap.PixelWidth, node.Height / bitmap.PixelHeight);
                        imageBox = new Rect((node.Width - bitmap.PixelWidth * ratio) / 2, (node.Height - bitmap.PixelHeight * ratio) / 2, bitmap.PixelWidth * ratio, bitmap.PixelHeight * ratio);
                    }
                    dc.DrawImage(bitmap, imageBox);
                    break;
                case ObjectKind.Text:
                    dc.DrawGeometry(Brush(node.Color), null, TextGeometry(node.Text, node.Font, node.FontSize)); break;
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
                    dc.DrawGeometry(null, pen, geometry);
                    Arrow(dc, node, start, node.PathKind == "直線" ? start - end : start - control, node.StartArrow);
                    Arrow(dc, node, end, node.PathKind == "直線" ? end - start : end - control, node.EndArrow);
                    break;
            }
        }
        var localBounds = drawing.Bounds;
        if (node.Kind == ObjectKind.Grid)
        {
            // Reserve the full grid and configured stroke overhang even when individual edges are empty.
            var reservation = new Rect(0, 0, node.Columns * node.CellSize, node.Rows * node.CellSize);
            var overhang = node.Children.Where(c => c.Visible && c.Kind == ObjectKind.Border).Select(c => c.Thickness / 2).DefaultIfEmpty(0).Max();
            reservation.Inflate(overhang, overhang); localBounds.Union(reservation);
        }
        if (!localBounds.IsEmpty)
        {
            var world = new MatrixTransform(Document.World(node)).TransformBounds(localBounds);
            Bounds[node.Id] = world;
            var content = ContentBounds; content.Union(world); ContentBounds = content;
        }
        drawing.Transform = LocalTransform(node); parent.DrawDrawing(drawing);
    }
    private static BitmapSource DocumentImage(byte[] bytes) => GridSketch.Document.DecodeImage(bytes);
    private static void DrawBorders(DrawingContext dc, SceneObject node, SceneObject grid, Pen pen)
    {
        void Run(bool vertical, int axis, int start, int end)
        {
            double s = grid.CellSize;
            dc.DrawLine(pen, vertical ? new Point(axis * s, start * s) : new Point(start * s, axis * s),
                vertical ? new Point(axis * s, end * s) : new Point(end * s, axis * s));
        }
        foreach (var group in SceneDocument.BorderEdges(node, grid).GroupBy(e => (e.Vertical, Axis: e.Vertical ? e.Column : e.Row)))
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
    }
    private static Geometry TextGeometry(string text, string font, double size) => new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(font), size, Brushes.Black, 1).BuildGeometry(new Point());
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
    public BitmapSource Export(int scale = 1)
    {
        if (scale is < 1 or > 4) throw new InvalidOperationException("出力倍率は1～4です。");
        int width = (int)Math.Ceiling(OutputBounds.Width * scale), height = (int)Math.Ceiling(OutputBounds.Height * scale);
        if (width < 1 || height < 1 || (long)width * height > 40_000_000) throw new InvalidOperationException("PNGは4000万画素以下にしてください。");
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(scale, scale)); dc.PushTransform(new TranslateTransform(-OutputBounds.X, -OutputBounds.Y));
            dc.PushClip(new RectangleGeometry(OutputBounds)); dc.DrawRectangle(Brush(Document.Canvas.Background), null, OutputBounds); dc.DrawDrawing(Drawing);
        }
        var rendered = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); rendered.Render(visual);
        var pixels = new byte[width * height * 4]; rendered.CopyPixels(pixels, width * 4, 0);
        // DPI is metadata: changing it must not resample or change pixel dimensions.
        return BitmapSource.Create(width, height, Document.Canvas.Dpi, Document.Canvas.Dpi, PixelFormats.Pbgra32, null, pixels, width * 4);
    }
}

public sealed class SceneSurface : FrameworkElement
{
    public const double Origin = 1024;
    public double OriginX { get; private set; } = Origin;
    public double OriginY { get; private set; } = Origin;
    public SceneRenderer? Renderer { get; private set; }
    public SceneObject? Selected { get; set; }
    public CellPosition? ActiveCell { get; set; }
    public bool TextVertical { get; set; }
    public CellPosition? RangeEnd { get; set; }
    public GridEdge? HoveredEdge { get; set; }
    public List<Glyph> Preview { get; set; } = [];
    public void Refresh(SceneDocument document)
    {
        Renderer = new SceneRenderer(document);
        var extents = Renderer.OutputBounds; extents.Union(Renderer.ContentBounds);
        OriginX = Math.Max(OriginX, Math.Ceiling(-extents.Left) + 128);
        OriginY = Math.Max(OriginY, Math.Ceiling(-extents.Top) + 128);
        Width = Math.Max(2400, extents.Right + OriginX + 300);
        Height = Math.Max(1800, extents.Bottom + OriginY + 300);
        InvalidateVisual();
    }
    public Point ToWorld(Point p) => new(p.X - OriginX, p.Y - OriginY);
    public Point ToLocal(SceneObject node, Point surfacePoint)
    {
        var inverse = Renderer!.Document.World(node); inverse.Invert(); return inverse.Transform(ToWorld(surfacePoint));
    }
    public CellPosition? CellAt(SceneObject grid, Point surfacePoint)
    {
        var p = ToLocal(grid, surfacePoint); int c = (int)Math.Floor(p.X / grid.CellSize), r = (int)Math.Floor(p.Y / grid.CellSize);
        return c >= 0 && r >= 0 && c < grid.Columns && r < grid.Rows ? new(c, r) : null;
    }
    public GridEdge? EdgeAt(SceneObject grid, Point surfacePoint, double zoom)
    {
        var p = ToLocal(grid, surfacePoint); double size = grid.CellSize, tolerance = Math.Min(size * 0.22, 7 / zoom);
        if (p.X < -tolerance || p.Y < -tolerance || p.X > grid.Columns * size + tolerance || p.Y > grid.Rows * size + tolerance) return null;
        int c = (int)Math.Round(p.X / size), r = (int)Math.Round(p.Y / size);
        double dx = Math.Abs(p.X - c * size), dy = Math.Abs(p.Y - r * size);
        if (Math.Min(dx, dy) > tolerance) return null;
        var edge = dx <= dy ? new GridEdge(c, Math.Clamp((int)Math.Floor(p.Y / size), 0, grid.Rows - 1), true) : new GridEdge(Math.Clamp((int)Math.Floor(p.X / size), 0, grid.Columns - 1), r, false);
        return edge.IsInside(grid.Columns, grid.Rows) ? edge : null;
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
        if (Selected is { } selected)
        {
            if (selected.Kind != ObjectKind.GridText && Renderer.Bounds.TryGetValue(selected.Id, out var selectedBounds)) dc.DrawRectangle(null, new Pen(Brushes.DodgerBlue, 1) { DashStyle = DashStyles.Dash }, selectedBounds);
            var grid = Renderer.Document.GridFor(selected);
            if (grid is not null)
            {
                dc.PushTransform(new MatrixTransform(Renderer.Document.World(grid)));
                if (ActiveCell is { } cell)
                {
                    var end = RangeEnd ?? cell;
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(25, 30, 100, 220)), new Pen(Brushes.DodgerBlue, 1), new Rect(Math.Min(cell.Column, end.Column) * grid.CellSize, Math.Min(cell.Row, end.Row) * grid.CellSize, (Math.Abs(cell.Column - end.Column) + 1) * grid.CellSize, (Math.Abs(cell.Row - end.Row) + 1) * grid.CellSize));
                    if (selected.Kind == ObjectKind.GridText && end == cell)
                    {
                        var p = new Point((cell.Column + 0.76) * grid.CellSize, (cell.Row + 0.76) * grid.CellSize);
                        var d = TextVertical ? new Vector(0, grid.CellSize * 0.17) : new Vector(grid.CellSize * 0.17, 0);
                        var side = new Vector(-d.Y, d.X) * 0.4;
                        var pen = new Pen(Brushes.DodgerBlue, 2);
                        dc.DrawLine(pen, p - d, p + d);
                        dc.DrawLine(pen, p + d, p + d * 0.3 + side);
                        dc.DrawLine(pen, p + d, p + d * 0.3 - side);
                    }
                }
                if (HoveredEdge is { } edge)
                {
                    var p = new Point(edge.Column * grid.CellSize, edge.Row * grid.CellSize);
                    dc.DrawLine(new Pen(Brushes.DodgerBlue, 3), p, new Point(p.X + (edge.Vertical ? 0 : grid.CellSize), p.Y + (edge.Vertical ? grid.CellSize : 0)));
                }
                foreach (var glyph in Preview)
                {
                    var rect = new Rect(glyph.Column * grid.CellSize, glyph.Row * grid.CellSize, grid.CellSize, grid.CellSize);
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(100, 255, 220, 70)), new Pen(Brushes.DarkOrange, 1), rect);
                    var text = new FormattedText(glyph.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(selected.Font), Math.Min(selected.FontSize, grid.CellSize * 0.7), Brushes.DarkOrange, 1);
                    dc.DrawText(text, new Point(rect.X + (rect.Width - text.Width) / 2, rect.Y + (rect.Height - text.Height) / 2));
                }
                dc.Pop();
            }
        }
        dc.Pop();
    }
}
