using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;

namespace GridSketch;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ObjectKind { Canvas, Group, Grid, Border, GridText, Fill, Image, Text, Line, Shape }

public sealed class Glyph
{
    public int Column { get; set; }
    public int Row { get; set; }
    public string Text { get; set; } = "";
    public string? Font { get; set; }
    public double? Size { get; set; }
}

public readonly record struct CellPosition(int Column, int Row);

public sealed class SceneObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ObjectKind Kind { get; set; }
    public string Name { get; set; } = "オブジェクト";
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public bool LayerExpanded { get; set; } = true;
    public double X { get; set; }
    public double Y { get; set; }
    public double Angle { get; set; }
    public List<SceneObject> Children { get; set; } = [];
    public double Width { get; set; } = 160;
    public double Height { get; set; } = 80;
    public bool AutoSize { get; set; } = true;
    public double Dpi { get; set; } = 96;
    public string CanvasUnit { get; set; } = "px";
    [JsonIgnore] public double CanvasPixelWidth => CanvasUnit == "mm" ? Math.Max(1, Math.Round(Width * Dpi / 25.4)) : Math.Ceiling(Width);
    [JsonIgnore] public double CanvasPixelHeight => CanvasUnit == "mm" ? Math.Max(1, Math.Round(Height * Dpi / 25.4)) : Math.Ceiling(Height);
    public double Padding { get; set; } = 8;
    public string Background { get; set; } = "#FFFFFF";
    public int Columns { get; set; } = 16;
    public int Rows { get; set; } = 12;
    public double CellSize { get; set; } = 48;
    public double Margin { get; set; } = 5;
    public string Color { get; set; } = "#162A40";
    public double Thickness { get; set; } = 2;
    public string LineStyle { get; set; } = "実線";
    public bool SquareCaps { get; set; }
    public bool JoinSegments { get; set; } = true;
    public string Pattern { get; set; } = "個別";
    public HashSet<GridEdge> Edges { get; set; } = [];
    public List<Glyph> Glyphs { get; set; } = [];
    public HashSet<CellPosition> FilledCells { get; set; } = [];
    public bool BlockInput { get; set; } = true;
    public string Font { get; set; } = "Yu Gothic";
    public double FontSize { get; set; } = 28;
    public string Text { get; set; } = "テキスト";
    public string Alignment { get; set; } = "中央";
    public byte[]? ImageData { get; set; }
    public bool SnapToCell { get; set; }
    public int Column { get; set; }
    public int Row { get; set; }
    public string Shape { get; set; } = "四角";
    public string FillColor { get; set; } = "#FFFFFF";
    public string PathKind { get; set; } = "直線";
    public double EndX { get; set; } = 120;
    public double EndY { get; set; } = 60;
    public double ControlX { get; set; } = 60;
    public double ControlY { get; set; } = -40;
    public string StartArrow { get; set; } = "なし";
    public string EndArrow { get; set; } = "三角";
    public double ArrowSize { get; set; } = 14;

    [JsonIgnore] public bool IsContainer => Kind is ObjectKind.Canvas or ObjectKind.Group or ObjectKind.Grid;
    [JsonIgnore] public bool IsGridContent => Kind is ObjectKind.Border or ObjectKind.GridText or ObjectKind.Fill || Kind == ObjectKind.Image && SnapToCell;
    [JsonIgnore] public string Label => $"{(Visible ? "" : "非表示 · ")}{(Locked ? "🔒 " : "")}{Name}  [{KindName(Kind)}]";
    public static string KindName(ObjectKind kind) => kind switch
    {
        ObjectKind.Canvas => "キャンバス", ObjectKind.Group => "グループ", ObjectKind.Grid => "グリッド",
        ObjectKind.Border => "罫線", ObjectKind.GridText => "グリッド文字", ObjectKind.Fill => "セル塗り",
        ObjectKind.Image => "画像", ObjectKind.Text => "フリーテキスト", ObjectKind.Line => "ライン", _ => "図形"
    };
}

public sealed class SceneDocument
{
    public PdfSettings PdfExport { get; set; } = new();
    public int Version { get; set; } = 3;
    public SceneObject Canvas { get; set; } = new() { Kind = ObjectKind.Canvas, Name = "キャンバス", Width = 800, Height = 600 };
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, MaxDepth = 128 };
    public string Snapshot() => JsonSerializer.Serialize(this, JsonOptions);
    public static SceneDocument Restore(string json) => JsonSerializer.Deserialize<SceneDocument>(json, JsonOptions) ?? throw new InvalidDataException("空のプロジェクトです。");
    public IEnumerable<SceneObject> All() => Walk(Canvas);
    private static IEnumerable<SceneObject> Walk(SceneObject node)
    {
        yield return node;
        foreach (var child in node.Children) foreach (var descendant in Walk(child)) yield return descendant;
    }
    public SceneObject? Find(string? id) => All().FirstOrDefault(n => n.Id == id);
    public SceneObject? Parent(SceneObject node) => All().FirstOrDefault(n => n.Children.Contains(node));
    public SceneObject? GridFor(SceneObject node) => node.Kind == ObjectKind.Grid ? node : Parent(node) is { Kind: ObjectKind.Grid } grid ? grid : null;
    public bool Editable(SceneObject node) => !node.Locked && (Parent(node) is not { } p || Editable(p));
    public bool Shown(SceneObject node) => node.Visible && (Parent(node) is not { } p || Shown(p));
    public Matrix World(SceneObject node)
    {
        var matrix = Matrix.Identity;
        if (node.Kind != ObjectKind.Canvas) { matrix.Rotate(node.Angle); matrix.Translate(node.X, node.Y); }
        if (Parent(node) is { } p) matrix.Append(World(p));
        return matrix;
    }

    public void Reparent(SceneObject node, SceneObject target)
    {
        if (node == Canvas || !target.IsContainer || node == target || Walk(node).Contains(target)) throw new InvalidOperationException("この親には移動できません。");
        if (node.IsGridContent) throw new InvalidOperationException("マスに配置されたオブジェクトはグリッド直下で管理します。");
        if (!Editable(node) || !Editable(target)) throw new InvalidOperationException("ロックされています。");
        var world = World(node); var inverse = World(target); inverse.Invert(); world.Append(inverse);
        Parent(node)!.Children.Remove(node); target.Children.Insert(0, node);
        node.X = world.OffsetX; node.Y = world.OffsetY; node.Angle = Math.Atan2(world.M12, world.M11) * 180 / Math.PI;
    }

    public static SceneObject CreateGrid(string preset, int columns = 16, int rows = 12, double cellSize = 48)
    {
        var grid = new SceneObject { Kind = ObjectKind.Grid, Name = preset, Columns = columns, Rows = rows, CellSize = cellSize };
        if (preset == "空のグリッド") return grid;
        grid.Children = [
            new() { Kind = ObjectKind.Border, Name = "外枠罫線", Pattern = "外枠", Thickness = 2 },
            new() { Kind = ObjectKind.Border, Name = "内枠罫線", Pattern = "内側", Thickness = 1, Color = preset == "基本の方眼紙" ? "#AAB8C5" : "#162A40" },
            new() { Kind = ObjectKind.GridText, Name = preset == "将棋盤" ? "駒の文字" : "グリッド文字" },
            new() { Kind = ObjectKind.Fill, Name = preset == "クロスワード" ? "黒マス" : "セル塗り", Color = "#000000", BlockInput = preset == "クロスワード" }
        ];
        return grid;
    }

    public static IEnumerable<GridEdge> BorderEdges(SceneObject border, SceneObject grid)
    {
        if (border.Pattern == "個別") return border.Edges;
        var edges = new List<GridEdge>();
        for (int c = 0; c <= grid.Columns; c++) for (int r = 0; r < grid.Rows; r++)
            if (border.Pattern == "すべて" || (c == 0 || c == grid.Columns ? border.Pattern == "外枠" : border.Pattern == "内側")) edges.Add(new(c, r, true));
        for (int r = 0; r <= grid.Rows; r++) for (int c = 0; c < grid.Columns; c++)
            if (border.Pattern == "すべて" || (r == 0 || r == grid.Rows ? border.Pattern == "外枠" : border.Pattern == "内側")) edges.Add(new(c, r, false));
        return edges;
    }

    public void ToggleEdge(SceneObject border, GridEdge edge)
    {
        var grid = GridFor(border) ?? throw new InvalidOperationException("グリッドがありません。");
        border.Edges = BorderEdges(border, grid).ToHashSet(); border.Pattern = "個別";
        if (!border.Edges.Remove(edge)) border.Edges.Add(edge);
    }

    public void ResizeGrid(SceneObject grid, int columns, int rows, double cellSize, double margin)
    {
        if (grid.Kind != ObjectKind.Grid) throw new InvalidOperationException("グリッドを選択してください。");
        if (columns is < 1 or > 200 || rows is < 1 or > 200 || !double.IsFinite(cellSize) || cellSize < 12 || cellSize > 256 || !double.IsFinite(margin) || margin < 0 || margin >= cellSize / 2)
            throw new InvalidOperationException("マス数は1～200、マス寸法は12～256、画像余白はマス寸法の半分未満にしてください。");
        bool Outside(int c, int r) => c >= columns || r >= rows;
        foreach (var child in grid.Children)
        {
            if (child.Kind == ObjectKind.GridText && child.Glyphs.Any(g => Outside(g.Column, g.Row)))
                throw new InvalidOperationException($"「{child.Name}」の文字が変更後の範囲外になります。文字を移動・削除してから縮小してください。");
            if (child.Kind == ObjectKind.Image && child.SnapToCell && Outside(child.Column, child.Row))
                throw new InvalidOperationException($"「{child.Name}」の画像が変更後の範囲外になります。配置マスを変更してから縮小してください。");
            if (child.Kind == ObjectKind.Fill && child.FilledCells.Any(c => Outside(c.Column, c.Row)))
                throw new InvalidOperationException($"「{child.Name}」の塗りが変更後の範囲外になります。該当する塗りを削除してから縮小してください。");
        }
        var resized = new SceneObject { Kind = ObjectKind.Grid, Columns = columns, Rows = rows };
        foreach (var border in grid.Children.Where(n => n.Kind == ObjectKind.Border))
        {
            if (border.Pattern != "個別") { border.Edges.Clear(); continue; }
            if (columns == grid.Columns && rows == grid.Rows) continue;
            // Legacy files and one-click border tools store complete frames as explicit edges.
            // Recognize those sets by geometry, not by a user-editable layer name.
            bool rebuilt = false;
            foreach (var pattern in new[] { "外枠", "内側", "すべて" })
            {
                foreach (bool? vertical in new bool?[] { null, true, false })
                {
                    IEnumerable<GridEdge> Edges(SceneObject g) => BorderEdges(new SceneObject { Pattern = pattern }, g).Where(e => vertical is null || e.Vertical == vertical);
                    var previous = Edges(grid).ToHashSet();
                    if (previous.Count == 0 || !border.Edges.SetEquals(previous)) continue;
                    border.Edges = Edges(resized).ToHashSet(); rebuilt = true; break;
                }
                if (rebuilt) break;
            }
            if (!rebuilt)
            {
                border.Edges = border.Edges.Select(e => new GridEdge(
                    e.Vertical && e.Column == grid.Columns ? columns : e.Column,
                    !e.Vertical && e.Row == grid.Rows ? rows : e.Row, e.Vertical))
                    .Where(e => e.IsInside(columns, rows)).ToHashSet();
            }
        }
        grid.Columns = columns; grid.Rows = rows; grid.CellSize = cellSize; grid.Margin = margin;
    }

    public List<Glyph> PlanText(SceneObject text, int column, int row, string input, bool vertical)
    {
        var grid = GridFor(text) ?? throw new InvalidOperationException("グリッド文字を選択してください。");
        if (input.Length == 0 || input.Any(char.IsControl)) throw new InvalidOperationException("改行や制御文字を含まない文字列を入力してください。");
        var result = new List<Glyph>(); var elements = StringInfo.GetTextElementEnumerator(input);
        while (elements.MoveNext())
        {
            if (column < 0 || row < 0 || column >= grid.Columns || row >= grid.Rows) throw new InvalidOperationException("文字列がグリッドの範囲を超えます。");
            if (grid.Children.Any(n => n.Kind == ObjectKind.Fill && n.BlockInput && n.FilledCells.Contains(new(column, row))))
                throw new InvalidOperationException($"{column + 1}列・{row + 1}行は黒マスです。");
            result.Add(new Glyph { Column = column, Row = row, Text = elements.GetTextElement() });
            if (vertical) row++; else column++;
        }
        return result;
    }

    public void Validate()
    {
        if (PdfExport is null) throw new InvalidDataException("PDF出力設定が不正です。");
        PdfExport.Validate();
        if (Canvas is not null && Canvas.CanvasUnit is not ("px" or "mm")) throw new InvalidDataException("キャンバスの単位はpxまたはmmです。");
        if (Version != 3 || Canvas is null || Canvas.Kind != ObjectKind.Canvas) throw new InvalidDataException("未対応のプロジェクト形式です。");
        var ids = new HashSet<string>();
        void Check(SceneObject node, SceneObject? parent, int depth)
        {
            if (node is null || depth > 24 || ids.Count > 5000 || string.IsNullOrWhiteSpace(node.Id) || !ids.Add(node.Id)) throw new InvalidDataException("階層が深すぎるか、IDが重複しています。");
            if (!Enum.IsDefined(node.Kind) || node.Children is null || node.Name is null || node.Name.Length > 200 || node.Glyphs is null || node.Edges is null || node.FilledCells is null) throw new InvalidDataException("オブジェクトが不正です。");
            if (parent is not null && (node.Kind == ObjectKind.Canvas || !parent.IsContainer)) throw new InvalidDataException("不正な親子構造です。");
            if (node.IsGridContent && (parent?.Kind != ObjectKind.Grid || node.X != 0 || node.Y != 0 || node.Angle != 0)) throw new InvalidDataException("マスに配置する内容はグリッド直下で管理し、個別に移動・回転できません。");
            foreach (var value in new[] { node.X, node.Y, node.Angle, node.Width, node.Height, node.Dpi, node.Padding, node.CellSize, node.Margin, node.Thickness, node.FontSize, node.EndX, node.EndY, node.ControlX, node.ControlY, node.ArrowSize })
                if (!double.IsFinite(value) || Math.Abs(value) > 100000) throw new InvalidDataException("数値が範囲外です。");
            if (node.Width < 1 || node.Height < 1 || node.Dpi < 1 || node.Dpi > 2400 || node.Padding < 0 || node.Padding > 1000 || node.Thickness < 0 || node.Thickness > 64 || node.FontSize < 1 || node.FontSize > 512 || node.ArrowSize < 1 || node.ArrowSize > 200) throw new InvalidDataException("サイズ・DPI・線幅が範囲外です。");
            foreach (var color in new[] { node.Color, node.FillColor, node.Background }) _ = (Color)ColorConverter.ConvertFromString(color);
            if (string.IsNullOrWhiteSpace(node.Font) || node.Text is null || node.Text.Length > 10000) throw new InvalidDataException("文字設定が不正です。");
            if (node.LineStyle is not ("実線" or "破線" or "点線") || node.Pattern is not ("個別" or "外枠" or "内側" or "すべて") || node.PathKind is not ("直線" or "折れ線" or "曲線") || node.Shape is not ("四角" or "角丸" or "楕円") || node.Alignment is not ("中央" or "左上") || node.StartArrow is not ("なし" or "三角" or "開いた矢印") || node.EndArrow is not ("なし" or "三角" or "開いた矢印")) throw new InvalidDataException("種類の設定が不正です。");
            if (node.Kind == ObjectKind.Grid && (node.Columns is < 1 or > 200 || node.Rows is < 1 or > 200 || node.CellSize < 12 || node.CellSize > 256 || node.Margin < 0 || node.Margin >= node.CellSize / 2)) throw new InvalidDataException("マス数は1～200、マス寸法は12～256、画像余白はマス寸法の半分未満にしてください。");
            if (node.IsGridContent && parent is { } grid)
            {
                bool In(int c, int r) => c >= 0 && r >= 0 && c < grid.Columns && r < grid.Rows;
                if (node.Edges.Any(e => !e.IsInside(grid.Columns, grid.Rows)) || node.FilledCells.Any(c => !In(c.Column, c.Row)) || node.Glyphs.Any(g => g is null || !In(g.Column, g.Row) || g.Text is null || new StringInfo(g.Text).LengthInTextElements != 1 || g.Text.Any(char.IsControl) || g.Size.HasValue && (!double.IsFinite(g.Size.Value) || g.Size < 1 || g.Size > 512))) throw new InvalidDataException("範囲外のマス・辺または不正な文字があります。縮小する場合は先に範囲外の内容を削除してください。");
                if (node.Glyphs.Select(g => (g.Column, g.Row)).Distinct().Count() != node.Glyphs.Count) throw new InvalidDataException("文字のマスが重複しています。");
                if (node.Kind == ObjectKind.Image && !In(node.Column, node.Row)) throw new InvalidDataException("画像の配置マスが範囲外です。");
            }
            if (node.ImageData is not null) _ = Document.DecodeImage(node.ImageData);
            foreach (var child in node.Children) Check(child, node, depth + 1);
        }
        Check(Canvas, null, 0);
    }

    public void Save(string path)
    {
        Validate(); var bytes = JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);
        if (bytes.Length > 100_000_000) throw new InvalidDataException("プロジェクトは100MB以下にしてください。");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static SceneDocument Load(string path)
    {
        if (new FileInfo(path).Length > 100_000_000) throw new InvalidDataException("プロジェクトは100MB以下にしてください。");
        var json = File.ReadAllText(path); using var parsed = JsonDocument.Parse(json);
        var version = parsed.RootElement.TryGetProperty("Version", out var v) ? v.GetInt32() : 1;
        var scene = version is 1 or 2 ? Migrate(Document.Load(path)) : Restore(json);
        scene.Validate(); return scene;
    }
    public static SceneDocument Migrate(Document old)
    {
        var scene = new SceneDocument();
        var grid = new SceneObject { Kind = ObjectKind.Grid, Name = "グリッド", Columns = old.Columns, Rows = old.Rows, CellSize = old.CellSize, Margin = old.Margin };
        double padding = Math.Ceiling(Math.Max(old.Strokes.Max(s => s.Width), old.WallStroke.Width) / 2) + 1;
        grid.X = grid.Y = padding;
        scene.Canvas.AutoSize = false; scene.Canvas.X = scene.Canvas.Y = 0;
        scene.Canvas.Width = grid.Columns * grid.CellSize + padding * 2; scene.Canvas.Height = grid.Rows * grid.CellSize + padding * 2;
        scene.Canvas.Children.Add(grid);
        SceneObject Border(string name, Stroke stroke, IEnumerable<GridEdge> edges) => new() { Kind = ObjectKind.Border, Name = name, Color = stroke.Color, Thickness = stroke.Width, LineStyle = stroke.Kind, Edges = edges.ToHashSet() };
        grid.Children.Add(Border("個別罫線", old.WallStroke, old.Walls));
        grid.Children[0].SquareCaps = true; grid.Children[0].JoinSegments = false;
        var all = BorderEdges(new SceneObject { Pattern = "すべて" }, grid).ToList();
        // Horizontal strokes were drawn after vertical strokes in the original renderer.
        foreach (int i in new[] { 1, 3, 0, 2 })
            grid.Children.Add(Border(new[] { "外枠・縦", "外枠・横", "内枠・縦", "内枠・横" }[i], old.Strokes[i], all.Where(e =>
                e.Vertical == (i % 2 == 0) && (e.Vertical ? e.Column == 0 || e.Column == grid.Columns : e.Row == 0 || e.Row == grid.Rows) == (i < 2))));
        grid.Children.Add(new SceneObject { Kind = ObjectKind.GridText, Name = "グリッド文字", Color = "#000000", Glyphs = old.Cells.Where(c => c.Text.Length > 0).Select(c => new Glyph { Column = c.Column, Row = c.Row, Text = c.Text, Font = c.Font, Size = c.FontSize }).ToList() });
        foreach (var cell in old.Cells.Where(c => c.Image is not null))
            grid.Children.Add(new SceneObject { Kind = ObjectKind.Image, Name = $"画像 {cell.Column + 1},{cell.Row + 1}", ImageData = cell.Image, SnapToCell = true, Column = cell.Column, Row = cell.Row });
        return scene;
    }
}
