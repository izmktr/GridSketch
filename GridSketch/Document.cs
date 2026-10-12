using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GridSketch;

public sealed class Stroke
{
    public string Color { get; set; } = "#AAB8C5";
    public double Width { get; set; } = 1;
    public string Kind { get; set; } = "実線";
}

public sealed class Cell
{
    public int Column { get; set; }
    public int Row { get; set; }
    public string Text { get; set; } = "";
    public string Font { get; set; } = "Yu Gothic";
    public double FontSize { get; set; } = 28;
    public byte[]? Image { get; set; }
}

// Shared edges have a single grid coordinate, regardless of which adjacent cell is used.
public readonly record struct GridEdge(int Column, int Row, bool Vertical)
{
    public bool IsInside(int columns, int rows) => Column >= 0 && Row >= 0 &&
        (Vertical ? Column <= columns && Row < rows : Column < columns && Row <= rows);
}

public sealed class Document
{
    public int Version { get; set; } = 2;
    public int Columns { get; set; } = 16;
    public int Rows { get; set; } = 12;
    public double CellSize { get; set; } = 48;
    public double Margin { get; set; } = 5;
    // 外周縦・外周横・内側縦・内側横
    public Stroke[] Strokes { get; set; } = [new() { Width = 2 }, new() { Width = 2 }, new(), new()];
    public List<Cell> Cells { get; set; } = [];
    public Stroke WallStroke { get; set; } = new() { Color = "#162A40", Width = 4 };
    public HashSet<GridEdge> Walls { get; set; } = [];

    public bool ToggleWall(GridEdge edge)
    {
        if (!edge.IsInside(Columns, Rows)) throw new ArgumentOutOfRangeException(nameof(edge));
        if (Walls.Remove(edge)) return false;
        Walls.Add(edge);
        return true;
    }

    public void Validate()
    {
        if (Version != 2) throw new InvalidDataException("未対応のプロジェクト形式です。");
        if (Columns is < 1 or > 200 || Rows is < 1 or > 200 || !double.IsFinite(CellSize) || CellSize < 12 || CellSize > 256)
            throw new InvalidDataException("列数・行数は1～200、マスの大きさは12～256pxで指定してください。");
        if (!double.IsFinite(Margin) || Margin < 0 || Margin >= CellSize / 2)
            throw new InvalidDataException("余白は0以上、マスの大きさの半分未満で指定してください。");
        if (Strokes is null || Strokes.Length != 4) throw new InvalidDataException("罫線設定が不正です。");
        if (WallStroke is null || WallStroke.Width <= 0 || Walls is null || Walls.Any(e => !e.IsInside(Columns, Rows)))
            throw new InvalidDataException("壁の位置または太さが不正です。");
        foreach (var stroke in Strokes.Append(WallStroke))
        {
            if (stroke is null || !double.IsFinite(stroke.Width) || stroke.Width < 0 || stroke.Width > 12 || stroke.Kind is not ("実線" or "破線" or "点線"))
                throw new InvalidDataException("罫線の太さは0～12px、線種は実線・破線・点線を指定してください。");
            _ = (Color)ColorConverter.ConvertFromString(stroke.Color);
        }
        if (Cells is null || Cells.Count > Columns * Rows || Cells.Any(c => c is null)) throw new InvalidDataException("マスの内容が不正です。");
        if (Cells.Select(c => (c.Column, c.Row)).Distinct().Count() != Cells.Count) throw new InvalidDataException("マスが重複しています。");
        foreach (var cell in Cells)
        {
            if (cell.Column < 0 || cell.Column >= Columns || cell.Row < 0 || cell.Row >= Rows || cell.Text is null || new StringInfo(cell.Text).LengthInTextElements > 1 || string.IsNullOrWhiteSpace(cell.Font) || !double.IsFinite(cell.FontSize) || cell.FontSize < 1 || cell.FontSize > 256)
                throw new InvalidDataException("文字または配置位置が不正です。");
            if (cell.Image is not null) _ = DecodeImage(cell.Image);
        }
    }

    public static BitmapSource DecodeImage(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        if ((long)bitmap.PixelWidth * bitmap.PixelHeight > 40_000_000) throw new InvalidDataException("画像は4000万画素以下にしてください。");
        bitmap.Freeze();
        return bitmap;
    }

    public static Document Load(string path)
    {
        if (new FileInfo(path).Length > 100_000_000) throw new InvalidDataException("プロジェクトは100MB以下にしてください。");
        var doc = JsonSerializer.Deserialize<Document>(File.ReadAllText(path)) ?? throw new InvalidDataException("プロジェクトが空です。");
        if (doc.Version == 1) doc.Version = 2;
        doc.Validate();
        return doc;
    }

    public void Save(string path)
    {
        Validate();
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(this, new JsonSerializerOptions { WriteIndented = true });
            if (json.Length > 100_000_000) throw new InvalidDataException("プロジェクトは100MB以下にしてください。");
            File.WriteAllBytes(temporary, json);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
