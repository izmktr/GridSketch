using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GridSketch;

public sealed class GridSurface : FrameworkElement
{
    public Document Document { get; set; } = new();
    public (int Column, int Row)? Selected { get; set; }
    public GridEdge? HoveredEdge { get; set; }
    // Reserve space for the configured wall width even before the first wall is placed.
    public double PaddingSize => Math.Ceiling(Math.Max(Document.Strokes.Max(s => s.Width),
        Document.WallStroke.Width) / 2) + 1;
    private readonly Dictionary<Cell, BitmapSource> images = [];

    public void Refresh(bool clearImages = false)
    {
        if (clearImages) images.Clear();
        Width = Document.Columns * Document.CellSize + 2 * PaddingSize;
        Height = Document.Rows * Document.CellSize + 2 * PaddingSize;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc) => Draw(dc, true);

    public GridEdge? EdgeAt(Point point, double zoom)
    {
        var size = Document.CellSize;
        double x = point.X - PaddingSize, y = point.Y - PaddingSize;
        double tolerance = Math.Min(size * 0.22, 7 / zoom);
        if (x < -tolerance || y < -tolerance || x > Document.Columns * size + tolerance || y > Document.Rows * size + tolerance) return null;
        int column = (int)Math.Round(x / size), row = (int)Math.Round(y / size);
        double dx = Math.Abs(x - column * size), dy = Math.Abs(y - row * size);
        if (Math.Min(dx, dy) > tolerance) return null;
        var edge = dx <= dy
            ? new GridEdge(column, Math.Clamp((int)Math.Floor(y / size), 0, Document.Rows - 1), true)
            : new GridEdge(Math.Clamp((int)Math.Floor(x / size), 0, Document.Columns - 1), row, false);
        return edge.IsInside(Document.Columns, Document.Rows) ? edge : null;
    }

    public void Draw(DrawingContext dc, bool selection)
    {
        var doc = Document;
        var size = doc.CellSize;
        var pad = PaddingSize;
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, Width, Height));
        foreach (var cell in doc.Cells)
        {
            var rect = new Rect(pad + cell.Column * size, pad + cell.Row * size, size, size);
            dc.PushClip(new RectangleGeometry(rect));
            if (cell.Image is not null)
            {
                if (!images.TryGetValue(cell, out var bitmap)) images[cell] = bitmap = Document.DecodeImage(cell.Image);
                var available = size - 2 * doc.Margin;
                var ratio = Math.Min(1, Math.Min(available / bitmap.PixelWidth, available / bitmap.PixelHeight));
                var w = bitmap.PixelWidth * ratio;
                var h = bitmap.PixelHeight * ratio;
                dc.DrawImage(bitmap, new Rect(rect.X + (size - w) / 2, rect.Y + (size - h) / 2, w, h));
            }
            if (cell.Text.Length > 0)
            {
                var text = new FormattedText(cell.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface(cell.Font), cell.FontSize, Brushes.Black, 1);
                var geometry = text.BuildGeometry(new Point());
                var bounds = geometry.Bounds;
                if (!bounds.IsEmpty)
                {
                    var scale = Math.Min(1, Math.Min((size - 2) / Math.Max(1, bounds.Width), (size - 2) / Math.Max(1, bounds.Height)));
                    dc.PushTransform(new TranslateTransform(rect.X + size / 2, rect.Y + size / 2));
                    dc.PushTransform(new ScaleTransform(scale, scale));
                    dc.PushTransform(new TranslateTransform(-bounds.X - bounds.Width / 2, -bounds.Y - bounds.Height / 2));
                    dc.DrawGeometry(Brushes.Black, null, geometry);
                    dc.Pop(); dc.Pop(); dc.Pop();
                }
            }
            dc.Pop();
        }
        void Line(int index, Point a, Point b)
        {
            var style = doc.Strokes[index];
            if (style.Width == 0) return;
            var pen = new Pen(new SolidColorBrush((Color)ColorConverter.ConvertFromString(style.Color)), style.Width)
            {
                DashStyle = style.Kind switch { "破線" => DashStyles.Dash, "点線" => DashStyles.Dot, _ => DashStyles.Solid }
            };
            dc.DrawLine(pen, a, b);
        }
        for (int c = 0; c <= doc.Columns; c++)
            Line(c == 0 || c == doc.Columns ? 0 : 2, new Point(pad + c * size, pad), new Point(pad + c * size, pad + doc.Rows * size));
        for (int r = 0; r <= doc.Rows; r++)
            Line(r == 0 || r == doc.Rows ? 1 : 3, new Point(pad, pad + r * size), new Point(pad + doc.Columns * size, pad + r * size));
        void DrawEdge(GridEdge edge, Pen pen)
        {
            var start = new Point(pad + edge.Column * size, pad + edge.Row * size);
            var end = new Point(start.X + (edge.Vertical ? 0 : size), start.Y + (edge.Vertical ? size : 0));
            dc.DrawLine(pen, start, end);
        }
        var wallPen = new Pen(new SolidColorBrush((Color)ColorConverter.ConvertFromString(doc.WallStroke.Color)), doc.WallStroke.Width)
        {
            StartLineCap = PenLineCap.Square, EndLineCap = PenLineCap.Square,
            DashStyle = doc.WallStroke.Kind switch { "破線" => DashStyles.Dash, "点線" => DashStyles.Dot, _ => DashStyles.Solid }
        };
        foreach (var edge in doc.Walls) DrawEdge(edge, wallPen);
        if (selection && HoveredEdge is { } hovered)
            DrawEdge(hovered, new Pen(Brushes.DodgerBlue, Math.Max(3, doc.WallStroke.Width + 2)));
        if (selection && Selected is { } selected)
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(25, 30, 100, 220)), new Pen(Brushes.RoyalBlue, 2),
                new Rect(pad + selected.Column * size + 1, pad + selected.Row * size + 1, size - 2, size - 2));
    }

    public BitmapSource Export(int scale)
    {
        var width = (int)Math.Ceiling(Width * scale);
        var height = (int)Math.Ceiling(Height * scale);
        if ((long)width * height > 40_000_000) throw new InvalidOperationException("PNGは4000万画素以下にしてください。マス数・サイズ・出力倍率を下げてください。");
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) Draw(dc, false);
        var bitmap = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }
}
