using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GridSketch;

internal static class ObjectLayoutChecks
{
    public static void Run(MainWindow window, string directory)
    {
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS: " + label); }
        bool Same(Matrix a, Matrix b) => new[] { a.M11-b.M11, a.M12-b.M12, a.M21-b.M21, a.M22-b.M22, a.OffsetX-b.OffsetX, a.OffsetY-b.OffsetY }.All(v => Math.Abs(v) < 1e-7);
        var scene = new SceneDocument();
        var grid = SceneDocument.CreateGrid("空のグリッド", 1, 1, 50); grid.Margin = grid.MarginY = 5;
        scene.Canvas.Children.Add(grid); scene.Canvas.Padding = 0;
        var selector = new SceneObject { Kind = ObjectKind.CellSelection, UseObjectLayout = true };
        grid.Children.Add(selector);
        var group = new SceneObject { Kind = ObjectKind.Group, HasLayoutFrame = true, Width = 100, Height = 100,
            UseParentLayout = true, LayoutRegion = "親の内側", LayoutSizing = "収める", LayoutAnchor = "中央" };
        selector.Children.Add(group); SceneDocument.AssignNewState(selector, group);
        SceneDocument.SetCellState(selector, new(0, 0), selector.States[0].Id);
        var foreground = new SceneObject { Kind = ObjectKind.Shape, X = 20, Y = 10, Width = 60, Height = 60, Thickness = 0, FillColor = "#0000FF" };
        var background = new SceneObject { Kind = ObjectKind.Shape, Width = 80, Height = 80, Thickness = 0, FillColor = "#FF0000" };
        group.Children = [foreground, background]; scene.Validate();
        Rect WorldRect(SceneObject node) => new MatrixTransform(scene.World(node)).TransformBounds(SceneDocument.OwnLayoutRect(node));
        Check(WorldRect(foreground) == new Rect(13, 9, 24, 24), "Fixed group coordinates fit to the cell with reserved right and bottom space");
        var before = scene.World(foreground); background.Width = 300; background.Height = 200;
        Check(Same(before, scene.World(foreground)), "Changing largest sibling never rescales other children");
        background.Width = background.Height = 80;
        group.LayoutHasInner = true; group.LayoutInsetX = 10; group.LayoutInsetY = 20;
        var organizer = new SceneObject { Kind = ObjectKind.Group, OrganizeOnly = true };
        group.Children.Remove(foreground); organizer.Children.Add(foreground); group.Children.Insert(0, organizer);
        Check(scene.ParentRegions(foreground).Inner == new Rect(10, 20, 80, 60), "Organizer passes the fixed frame inner and outer regions through");
        foreground.UseParentLayout = true; foreground.LayoutRegion = "親の内側"; foreground.LayoutSizing = "広げる";
        foreground.X = foreground.Y = 0;
        Check(WorldRect(foreground) == new Rect(9, 13, 32, 24), "Child layout uses group coordinates before the group fit");
        foreground.UseParentLayout = false; foreground.X = 20; foreground.Y = 10;
        var pixels = new byte[40 * 20 * 4]; for (int i = 0; i < pixels.Length; i += 4) { pixels[i+1] = 255; pixels[i+3] = 255; }
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(40, 20, 96, 96, PixelFormats.Bgra32, null, pixels, 160)));
        using var memory = new MemoryStream(); encoder.Save(memory);
        var image = new SceneObject { Kind = ObjectKind.Image, ImageData = memory.ToArray(), Width = 60, Height = 60, X = 20, Y = 10 };
        group.Children.Insert(0, image);
        Check(new SceneRenderer(scene).ObjectTemplate(image).Bounds == new Rect(20, 25, 60, 30), "Image preserves its aspect inside its own logical rectangle");
        var file = Path.Combine(directory, "object-layout.gridsketch"); scene.Save(file);
        Check(SceneDocument.Load(file).Snapshot() == scene.Snapshot(), "Object frames and layout survive project round trip");
        var partFile = Path.Combine(directory, "object-layout.gspart"); PartFile.Export(LibraryPart.FromGroup(group, "固定範囲"), partFile);
        var copy = PartFile.Import(partFile).CreateCopy();
        Check(copy.HasLayoutFrame && copy.Width == 100 && copy.LayoutInsetY == 20 && copy.UseParentLayout, "Parts preserve their coordinate frames and parent layout");
        encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(new SceneRenderer(scene).Export()));
        using (var output = File.Create(Path.Combine(directory, "object-layout.png"))) encoder.Save(output);
        PdfExporter.Save(scene, Path.Combine(directory, "object-layout.pdf"));

        var standalone = new SceneDocument(); standalone.Canvas.AutoSize = false; standalone.Canvas.Width = 200; standalone.Canvas.Height = 100;
        var parent = new SceneObject { Kind = ObjectKind.Group, HasLayoutFrame = true, Width = 100, Height = 100, UseParentLayout = true, LayoutSizing = "広げる" };
        var shape = new SceneObject { Kind = ObjectKind.Shape, X = 10, Y = 15, Angle = 30 };
        standalone.Canvas.Children.Add(parent); parent.Children.Add(shape);
        Check(standalone.EffectiveLocalMatrix(parent).M11 == 2, "Fixed canvas provides a fitting target");
        before = standalone.World(shape); standalone.Reparent(shape, standalone.Canvas);
        Check(Same(before, standalone.World(shape)) && shape.PreservedTransform is not null, "Reparent preserves scale and rotation under a stretched parent");
        var roundTrip = SceneDocument.Restore(standalone.Snapshot());
        Check(Same(before, roundTrip.World(roundTrip.Find(shape.Id)!)), "Preserved transforms survive serialization");
        standalone.Canvas.AutoSize = true;
        Check(standalone.ParentRegions(parent).Outer is null && standalone.EffectiveLocalMatrix(parent).IsIdentity, "Auto canvas disables parent fit to avoid a size dependency cycle");
        var gridGroup = new SceneObject { Kind = ObjectKind.Group, HasLayoutFrame = true, Width = 100, Height = 100 };
        grid.Children.Add(gridGroup);
        var fitted = new SceneObject { Kind = ObjectKind.Shape, Width = 20, Height = 20, UseParentLayout = true, LayoutSizing = "収める" };
        gridGroup.Children.Add(fitted); before = scene.World(fitted);
        gridGroup.Children.Add(new SceneObject { Kind = ObjectKind.GridText }); scene.PrepareGroupLayouts();
        Check(gridGroup.OrganizeOnly && Same(before, scene.World(fitted)), "Automatic organizing conversion preserves existing child placement");
        scene.Validate();

        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        void Call(string name) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, null);
        Set("document", scene); Set("selectedId", group.Id); Call("RefreshAll");
        var panel = (StackPanel)window.FindName("Properties");
        PropertyChecks.Find<ComboBox>(panel, "配置先").SelectedItem = "親の外側";
        PropertyChecks.Find<ComboBox>(panel, "サイズの扱い").SelectedItem = "広げる";
        panel.Children.OfType<Button>().Single(b => Equals(b.Content, "プロパティを適用")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(group.LayoutRegion == "親の外側" && group.LayoutSizing == "広げる", "Parent layout controls apply together");
        Call("Undo"); var restored = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        Check(restored.Find(group.Id)!.LayoutRegion == "親の内側", "Parent layout controls support undo"); Set("savedState", restored.Snapshot());
    }
}
