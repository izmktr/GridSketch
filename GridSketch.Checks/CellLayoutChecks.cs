using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GridSketch;

internal static class CellLayoutChecks
{
    public static void Run(MainWindow window, string directory)
    {
        var scene = new SceneDocument();
        var grid = SceneDocument.CreateGrid("配置検証", 1, 1, 100);
        grid.Margin = 10; grid.MarginY = 20;
        scene.Canvas.Children.Add(grid); scene.Canvas.Padding = 0;
        var selector = new SceneObject { Kind = ObjectKind.CellSelection, UseCellLayout = true };
        grid.Children.Add(selector);
        var data = new byte[40 * 20 * 4];
        for (int i = 0; i < data.Length; i += 4) { data[i] = 255; data[i + 3] = 255; }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(40, 20, 96, 96, PixelFormats.Bgra32, null, data, 160)));
        using var stream = new MemoryStream(); encoder.Save(stream);
        var image = new SceneObject { Kind = ObjectKind.Image, ImageData = stream.ToArray(), Width = 40, Height = 20 };
        var background = new SceneObject { Kind = ObjectKind.Shape, FillColor = "#FF0000", Thickness = 0,
            Width = 30, Height = 40, TemplateRegion = "マス全体", TemplateSizing = "親いっぱい" };
        var group = new SceneObject { Kind = ObjectKind.Group, Children = [image, background] };
        selector.Children.Add(group); SceneDocument.AssignNewState(selector, group);
        SceneDocument.SetCellState(selector, new(0, 0), selector.States[0].Id);
        scene.Validate();
        Rect Bounds(SceneObject node) => new SceneRenderer(scene).CellTemplate(node, grid).Bounds;
        void Expect(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS: " + label); }
        Expect(Bounds(image) == new Rect(10, 30, 80, 40), "Image fits inset region with aspect preserved");
        Expect(Bounds(background) == new Rect(0, 0, 100, 100), "Background fills full cell");
        byte[] Pixels()
        {
            var bitmap = new SceneRenderer(scene).Export();
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); return pixels;
        }
        var before = Pixels(); background.Width = 300; background.Height = 7;
        Expect(before.SequenceEqual(Pixels()), "Background dimensions do not change sibling image or composite");
        grid.MarginY = 40;
        Expect(Bounds(image) == new Rect(30, 40, 40, 20) && Bounds(background) == new Rect(0, 0, 100, 100), "Vertical margins affect only inset content");
        image.TemplateSizing = "固定サイズ"; grid.CellSize = 120;
        Expect(Bounds(image) == new Rect(40, 50, 40, 20), "Fixed size survives cell size changes");
        image.TemplateAnchor = "左上"; image.X = 3; image.Y = 4;
        Expect(Bounds(image) == new Rect(13, 44, 40, 20), "Anchor and offset place image independently");
        image.TemplateSizing = "親いっぱい"; image.X = image.Y = 0;
        Expect(Bounds(image) == new Rect(10, 40, 100, 40), "Stretch uses both region dimensions");
        var nested = new SceneObject { Kind = ObjectKind.Group, Children = [image] };
        group.Children[0] = nested;
        Expect(Bounds(nested) == Bounds(image), "Nested groups pass both regions through");
        nested.X = 2;
        Expect(Bounds(nested).X == Bounds(image).X + 2, "Group translation affects children together");
        nested.X = 0; image.TemplateSizing = "収める"; image.TemplateAnchor = "中央";
        var file = Path.Combine(directory, "cell-layout.gridsketch"); scene.Save(file);
        Expect(SceneDocument.Load(file).Snapshot() == scene.Snapshot(), "Cell layout settings persist");
        Expect(!new SceneObject().UseCellLayout, "Older templates retain legacy layout by default");
        encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(new SceneRenderer(scene).Export()));
        using (var output = File.Create(Path.Combine(directory, "cell-layout.png"))) encoder.Save(output);
        PdfExporter.Save(scene, Path.Combine(directory, "cell-layout.pdf"));
        var partPath = Path.Combine(directory, "cell-layout.gspart");
        PartFile.Export(LibraryPart.FromGroup(group, "配置部品"), partPath);
        var copy = PartFile.Import(partPath).CreateCopy();
        Expect(copy.Children[1].TemplateRegion == "マス全体" && copy.Children[1].TemplateSizing == "親いっぱい",
            "Part import and copy preserve per-child layout");
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        void Call(string name) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, null);
        Set("document", scene); Set("selectedId", image.Id); Call("RefreshAll");
        var panel = (StackPanel)window.FindName("Properties");
        PropertyChecks.Find<ComboBox>(panel, "配置領域").SelectedItem = "マス全体";
        PropertyChecks.Find<ComboBox>(panel, "領域への収め方").SelectedItem = "固定サイズ";
        panel.Children.OfType<Button>().Single(b => Equals(b.Content, "プロパティを適用")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Expect(image.TemplateRegion == "マス全体" && image.TemplateSizing == "固定サイズ", "Layout properties apply from UI");
        Call("Undo");
        var restored = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        Expect(restored.Find(image.Id)!.TemplateRegion == "余白の内側", "Layout changes undo together");
        Set("savedState", restored.Snapshot());
    }
}
