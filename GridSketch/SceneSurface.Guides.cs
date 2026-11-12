using System.Windows;
using System.Windows.Media;

namespace GridSketch;

public sealed partial class SceneSurface
{
    public string GuideMode { get; set; } = "自動";
    public bool CleanPreview { get; set; }
    public CellPosition? HoveredCell { get; set; }

    public IEnumerable<SceneObject> GuideGrids()
    {
        if (Renderer is null || CleanPreview || GuideMode == "非表示") return [];
        var doc = Renderer.Document;
        if (GuideMode == "常に表示") return doc.All().Where(n => n.Kind == ObjectKind.Grid && doc.Shown(n));
        return !ShowSelectionBounds && Selected is { IsGridContent: true } node && doc.Shown(node) && doc.Editable(node)
            && doc.GridFor(node) is { } grid ? [grid] : [];
    }

    private void DrawGridGuides(DrawingContext dc)
    {
        if (Renderer is null) return;
        double zoom = Math.Max(0.01, Math.Abs(LayoutTransform.Value.M11));
        var background = ((SolidColorBrush)SceneRenderer.Brush(Renderer.Document.Canvas.Background)).Color;
        double alpha = background.A / 255d;
        bool dark = alpha * (0.2126 * background.R + 0.7152 * background.G + 0.0722 * background.B) + (1 - alpha) * 255 < 128;
        var brush = new SolidColorBrush(dark ? Color.FromArgb(105, 225, 230, 235) : Color.FromArgb(85, 70, 85, 100));
        var pen = new Pen(brush, 1 / zoom) { DashStyle = DashStyles.Dot };
        foreach (var grid in GuideGrids())
        {
            var matrix = Renderer.Document.World(grid);
            Point At(double x, double y) => matrix.Transform(new Point(x * grid.CellSize, y * grid.CellHeight));
            for (int c = 0; c <= grid.Columns; c++) dc.DrawLine(pen, At(c, 0), At(c, grid.Rows));
            for (int r = 0; r <= grid.Rows; r++) dc.DrawLine(pen, At(0, r), At(grid.Columns, r));
            if (Selected is { ConnectCellCenters: true } selected && Renderer.Document.GridFor(selected) == grid
                && (At(1, 0) - At(0, 0)).Length * zoom >= 6 && (At(0, 1) - At(0, 0)).Length * zoom >= 6)
                for (int c = 0; c < grid.Columns; c++) for (int r = 0; r < grid.Rows; r++)
                    dc.DrawEllipse(brush, null, At(c + 0.5, r + 0.5), 1.5 / zoom, 1.5 / zoom);
        }
        if (!ShowSelectionBounds && HoveredCell is { } cell && Selected is { Kind: ObjectKind.GridText } text
            && Renderer.Document.Shown(text) && Renderer.Document.Editable(text) && Renderer.Document.GridFor(text) is { } textGrid)
        {
            var rect = SceneDocument.CellRect(textGrid, cell, false);
            var geometry = new RectangleGeometry(rect) { Transform = new MatrixTransform(Renderer.Document.World(textGrid)) };
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(20, 30, 144, 255)), new Pen(Brushes.DodgerBlue, 1 / zoom), geometry);
        }
    }
}
