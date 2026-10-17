using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GridSketch;

// Exports the same WPF drawing used by the editor. Text is already outlined by SceneRenderer.
public sealed class PdfExporter
{
    private readonly List<byte[]> objects = [];
    private readonly StringBuilder content = new();
    private readonly Dictionary<byte, int> alphas = [];
    private readonly List<int> images = [];
    private readonly PdfSettings settings;
    private PdfExporter(PdfSettings settings) { this.settings = settings; }
    private static string N(double n) => n.ToString("0.########", CultureInfo.InvariantCulture);
    private void Emit(string command) => content.AppendLine(command);
    private int Add(string value) => Add(Encoding.ASCII.GetBytes(value));
    private int Add(byte[] value) { objects.Add(value); return objects.Count; }
    private static byte[] Deflate(byte[] bytes)
    {
        using var output = new MemoryStream(); using (var zip = new ZLibStream(output, CompressionLevel.Optimal, true)) zip.Write(bytes); return output.ToArray();
    }
    private int Stream(string dictionary, byte[] bytes)
    {
        using var stream = new MemoryStream();
        stream.Write(Encoding.ASCII.GetBytes($"<< {dictionary} /Length {bytes.Length} >>\nstream\n")); stream.Write(bytes); stream.Write(Encoding.ASCII.GetBytes("\nendstream")); return Add(stream.ToArray());
    }
    public static Size PageSize(SceneRenderer renderer)
    {
        var canvas = renderer.Document.Canvas;
        return !canvas.AutoSize && canvas.CanvasUnit == "mm"
            ? new Size(canvas.Width * 72 / 25.4, canvas.Height * 72 / 25.4)
            : new Size(renderer.OutputBounds.Width * 72 / canvas.Dpi, renderer.OutputBounds.Height * 72 / canvas.Dpi);
    }
    public static void Save(SceneDocument document, string path)
    {
        document.Validate(); var exporter = new PdfExporter(document.PdfExport);
        var bytes = exporter.Build(new SceneRenderer(document));
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private byte[] Build(SceneRenderer renderer)
    {
        var size = PageSize(renderer); var bounds = renderer.OutputBounds;
        if (size.Width > 14400 || size.Height > 14400) throw new InvalidOperationException("PDFの用紙は各辺5080mm以下にしてください。キャンバスのサイズまたはdpiを調整してください。");
        Add("<< /Type /Catalog /Pages 2 0 R >>"); Add("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"); Add("");
        var pageTransform = new Matrix(size.Width / bounds.Width, 0, 0, -size.Height / bounds.Height, -bounds.X * size.Width / bounds.Width, size.Height + bounds.Y * size.Height / bounds.Height);
        Emit("q"); Transform(pageTransform); Path(new RectangleGeometry(bounds)); Emit("W n");
        Fill(new RectangleGeometry(bounds), SceneRenderer.Brush(renderer.Document.Canvas.Background));
        Draw(renderer.Drawing, pageTransform); Emit("Q");
        int commands = Stream("/Filter /FlateDecode", Deflate(Encoding.ASCII.GetBytes(content.ToString())));
        var resources = $"/ExtGState << {string.Join(" ", alphas.Select(pair => $"/A{pair.Key} {pair.Value} 0 R"))} >> /XObject << {string.Join(" ", images.Select((id, i) => $"/I{i} {id} 0 R"))} >>";
        objects[2] = Encoding.ASCII.GetBytes($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(size.Width)} {N(size.Height)}] /Resources << {resources} >> /Contents {commands} 0 R /Group << /Type /Group /S /Transparency /CS /DeviceRGB >> >>");
        using var output = new MemoryStream();
        void Write(string s) => output.Write(Encoding.ASCII.GetBytes(s));
        Write("%PDF-1.4\n%GridSketch\n"); var offsets = new List<long> { 0 };
        for (int i = 0; i < objects.Count; i++) { offsets.Add(output.Position); Write($"{i + 1} 0 obj\n"); output.Write(objects[i]); Write("\nendobj\n"); }
        long xref = output.Position; Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (long offset in offsets.Skip(1)) Write(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"); return output.ToArray();
    }
    private void Transform(Matrix m) => Emit($"{N(m.M11)} {N(m.M12)} {N(m.M21)} {N(m.M22)} {N(m.OffsetX)} {N(m.OffsetY)} cm");
    private void Draw(Drawing drawing, Matrix world)
    {
        switch (drawing)
        {
            case DrawingGroup group:
                Emit("q"); var local = group.Transform?.Value ?? Matrix.Identity; Transform(local); local.Append(world);
                if (group.ClipGeometry is { } clip) { var rule = Path(clip); Emit(rule == FillRule.EvenOdd ? "W* n" : "W n"); }
                foreach (var child in group.Children) Draw(child, local);
                Emit("Q"); break;
            case GeometryDrawing geometry:
                if (geometry.Brush is not null) Fill(geometry.Geometry, geometry.Brush);
                if (geometry.Pen is { Thickness: > 0 } pen)
                    Fill(geometry.Geometry.GetWidenedPathGeometry(pen, 0.02, ToleranceType.Absolute), pen.Brush);
                break;
            case ImageDrawing image when image.ImageSource is BitmapSource bitmap:
                Image(bitmap, image.Rect, world); break;
            default: throw new InvalidOperationException("PDFで未対応の描画形式です。");
        }
    }
    private FillRule Path(Geometry geometry)
    {
        var path = geometry.GetFlattenedPathGeometry(0.02, ToleranceType.Absolute); var transform = path.Transform?.Value ?? Matrix.Identity;
        void Point(Point point, string op) { point = transform.Transform(point); Emit($"{N(point.X)} {N(point.Y)} {op}"); }
        foreach (var figure in path.Figures)
        {
            Point(figure.StartPoint, "m");
            foreach (var segment in figure.Segments)
            {
                if (segment is PolyLineSegment lines) foreach (var p in lines.Points) Point(p, "l");
                else if (segment is LineSegment line) Point(line.Point, "l");
                else throw new InvalidOperationException("PDFパスの変換に失敗しました。");
            }
            if (figure.IsClosed) Emit("h");
        }
        return path.FillRule;
    }
    private void Alpha(byte alpha)
    {
        if (!alphas.TryGetValue(alpha, out var id)) alphas[alpha] = Add($"<< /Type /ExtGState /ca {N(alpha / 255.0)} /CA {N(alpha / 255.0)} >>");
        Emit($"/A{alpha} gs");
    }
    private void Fill(Geometry geometry, Brush brush)
    {
        if (brush is not SolidColorBrush solid) throw new InvalidOperationException("PDFで未対応のブラシです。");
        var color = solid.Color; if (color.A == 0) return;
        Alpha((byte)Math.Round(color.A * solid.Opacity)); Emit($"{N(color.R / 255.0)} {N(color.G / 255.0)} {N(color.B / 255.0)} rg");
        Emit(Path(geometry) == FillRule.EvenOdd ? "f*" : "f");
    }
    private void Image(BitmapSource source, Rect rect, Matrix world)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        if (settings.MaxImageDpi > 0)
        {
            var vx = world.Transform(new Vector(rect.Width, 0)); var vy = world.Transform(new Vector(0, rect.Height));
            double scale = Math.Min(1, Math.Max(vx.Length * settings.MaxImageDpi / 72 / source.PixelWidth, vy.Length * settings.MaxImageDpi / 72 / source.PixelHeight));
            if (scale < 1) source = new TransformedBitmap(source, new ScaleTransform(Math.Max(1.0 / source.PixelWidth, scale), Math.Max(1.0 / source.PixelHeight, scale)));
        }
        int w = source.PixelWidth, h = source.PixelHeight;
        if ((long)w * h > 40_000_000) throw new InvalidOperationException("PDF内の画像は4000万画素以下にしてください。");
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0); var pixels = new byte[w * h * 4]; bgra.CopyPixels(pixels, w * 4, 0);
        var rgb = new byte[w * h * 3]; var mask = new byte[w * h]; bool transparent = false;
        for (int i = 0; i < w * h; i++) { rgb[i * 3] = pixels[i * 4 + 2]; rgb[i * 3 + 1] = pixels[i * 4 + 1]; rgb[i * 3 + 2] = pixels[i * 4]; mask[i] = pixels[i * 4 + 3]; transparent |= mask[i] != 255; }
        string common = $"/Type /XObject /Subtype /Image /Width {w} /Height {h} /BitsPerComponent 8";
        int maskId = transparent ? Stream(common + " /ColorSpace /DeviceGray /Filter /FlateDecode", Deflate(mask)) : 0;
        byte[] encoded; string filter;
        if (settings.JpegCompression)
        {
            var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Rgb24, null, rgb, w * 3);
            var encoder = new JpegBitmapEncoder { QualityLevel = settings.JpegQuality }; encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = new MemoryStream(); encoder.Save(stream); encoded = stream.ToArray(); filter = "/DCTDecode";
        }
        else { encoded = Deflate(rgb); filter = "/FlateDecode"; }
        int id = Stream(common + $" /ColorSpace /DeviceRGB /Filter {filter}" + (transparent ? $" /SMask {maskId} 0 R" : ""), encoded);
        int index = images.Count; images.Add(id);
        Emit("q"); Alpha(255); Transform(new Matrix(rect.Width, 0, 0, -rect.Height, rect.X, rect.Bottom)); Emit($"/I{index} Do"); Emit("Q");
    }
}
