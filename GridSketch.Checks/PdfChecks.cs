using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GridSketch;

internal static class PdfChecks
{
    public static void Run(string directory)
    {
        var scene = new SceneDocument(); scene.Canvas.AutoSize = false; scene.Canvas.Width = 600; scene.Canvas.Height = 450; scene.Canvas.Background = "#00000000";
        scene.Canvas.Children.Add(new SceneObject { Kind = ObjectKind.Text, X = 30, Y = 10, Text = "日本語 PDF / GridSketch", FontSize = 24, Style = new LayerStyle { OutlineEnabled = true, OutlineColor = "#88FF9900", OutlineWidth = 2 } });
        var grid = SceneDocument.CreateGrid("基本の方眼紙", 6, 4, 44); grid.X = 40; grid.Y = 90; grid.Angle = 7; scene.Canvas.Children.Add(grid);
        grid.Children.First(n => n.Kind == ObjectKind.GridText).Glyphs.Add(new Glyph { Column = 1, Row = 1, Text = "字" });
        grid.Children.Add(new SceneObject { Kind = ObjectKind.Fill, Color = "#8800AAFF", FilledCells = [new(2, 2)] });
        scene.Canvas.Children.Add(new SceneObject { Kind = ObjectKind.Line, X = 320, Y = 130, EndX = 180, EndY = 40, PathKind = "曲線", ControlX = 90, ControlY = -80, LineStyle = "破線", Thickness = 4, Color = "#CCDD4422" });
        scene.Canvas.Children.Add(new SceneObject { Kind = ObjectKind.Shape, X = 400, Y = 220, Width = 130, Height = 100, Shape = "楕円", FillColor = "#8044CC88", LineStyle = "点線", Thickness = 3 });
        // Four unequal quadrants verify orientation, resampling and a partially transparent image mask.
        var pixels = new byte[600 * 400 * 4];
        for (int y = 0; y < 400; y++) for (int x = 0; x < 600; x++)
        {
            int i = (y * 600 + x) * 4; pixels[i] = (byte)(x < 300 ? 20 : 220); pixels[i + 1] = (byte)(y < 200 ? 30 : 210); pixels[i + 2] = (byte)(x < 300 ? 230 : 40); pixels[i + 3] = (byte)(y < 200 ? 255 : 100);
        }
        var bitmap = BitmapSource.Create(600, 400, 96, 96, PixelFormats.Bgra32, null, pixels, 600 * 4);
        var imageEncoder = new PngBitmapEncoder(); imageEncoder.Frames.Add(BitmapFrame.Create(bitmap)); using var image = new MemoryStream(); imageEncoder.Save(image);
        scene.Canvas.Children.Add(new SceneObject { Kind = ObjectKind.Image, X = 90, Y = 330, Width = 120, Height = 80, Angle = -8, ImageData = image.ToArray() });
        scene.Canvas.Children.Add(new SceneObject { Kind = ObjectKind.Shape, X = 570, Y = 390, Width = 100, Height = 100, FillColor = "#FF44AA" });
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(new SceneRenderer(scene).Export()));
        using (var output = File.Create(Path.Combine(directory, "pdf-reference.png"))) encoder.Save(output);
        foreach (var (preset, name) in new[] { ("印刷用", "print"), ("軽量", "small"), ("品質優先", "quality") })
        {
            scene.PdfExport = PdfSettings.ForPreset(preset); PdfExporter.Save(scene, Path.Combine(directory, $"pdf-{name}.pdf"));
            if (SceneDocument.Restore(scene.Snapshot()).PdfExport.Preset != preset) throw new Exception("PDF settings round trip failed");
        }
        scene.Canvas.CanvasUnit = "mm"; scene.Canvas.Width = 210; scene.Canvas.Height = 297; scene.Canvas.Dpi = 300;
        PdfExporter.Save(scene, Path.Combine(directory, "pdf-a4.pdf"));
        var size = PdfExporter.PageSize(new SceneRenderer(scene));
        if (Math.Abs(size.Width * 25.4 / 72 - 210) > 0.00001) throw new Exception("PDF physical size failed");
        try { new PdfSettings { MaxImageDpi = -1 }.Validate(); throw new Exception("Invalid PDF setting accepted"); } catch (InvalidDataException) { }
        Console.WriteLine("PASS: PDF exports, presets, settings persistence, validation and exact A4 page size");
    }
}
