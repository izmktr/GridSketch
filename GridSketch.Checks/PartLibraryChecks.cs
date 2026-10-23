using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GridSketch;

internal static class PartLibraryChecks
{
    public static void Run(MainWindow window, string directory)
    {
        var source = new SceneObject { Kind = ObjectKind.Group, Name = "画像と背景", X = 123, Y = 456, Angle = 15 };
        var raw = new byte[16 * 16 * 4];
        for (int i = 0; i < raw.Length; i += 4) { raw[i] = 255; raw[i + 3] = 255; }
        var bitmap = BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null, raw, 64);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var imageStream = new MemoryStream(); png.Save(imageStream);
        source.Children.Add(new() { Kind = ObjectKind.Image, Name = "青い画像", ImageData = imageStream.ToArray(), Width = 36, Height = 36, X = 10, Y = 10 });
        source.Children.Add(new() { Kind = ObjectKind.Shape, Name = "赤い背景", Width = 60, Height = 60, FillColor = "#FF0000", Thickness = 0 });
        var part = LibraryPart.FromGroup(source, "画像＋背景");
        if (part.Template.X != 0 || part.Template.Y != 0 || part.Template.Angle != 15 || source.X != 123) throw new Exception("Part origin normalization failed");
        var scene = new SceneDocument(); scene.Parts.Add(part);
        var a = scene.InsertPart(part, scene.Canvas); var b = scene.InsertPart(part, scene.Canvas);
        if (a.Id == b.Id || a.Children[0].Id == b.Children[0].Id || a.Children[0].Id == part.Template.Children[0].Id) throw new Exception("Copies share IDs");
        a.Children[0].ImageData![0] = 0; a.Children[1].FillColor = "#00FF00";
        if (b.Children[0].ImageData![0] == 0 || part.Template.Children[1].FillColor != "#FF0000") throw new Exception("Copies share mutable data");
        scene.Canvas.Children.Remove(a); // Deliberately corrupted image bytes were only an aliasing check.
        var path = Path.Combine(directory, "sample.gspart"); PartFile.Export(part, path);
        var imported = PartFile.Import(path); var again = PartFile.Import(path);
        if (imported.Id == again.Id || imported.Template.Id == again.Template.Id || !imported.Template.Children[0].ImageData!.SequenceEqual(part.Template.Children[0].ImageData!)) throw new Exception("Image import or independent import failed");
        scene.Parts.Add(imported); scene.Save(Path.Combine(directory, "parts.gridsketch"));
        var loaded = SceneDocument.Load(Path.Combine(directory, "parts.gridsketch"));
        if (loaded.Snapshot() != scene.Snapshot()) throw new Exception("Library project persistence failed");
        var legacy = JsonNode.Parse(scene.Snapshot())!; legacy.AsObject().Remove("Parts");
        if (SceneDocument.Restore(legacy.ToJsonString()).Parts.Count != 0) throw new Exception("Old project library default failed");
        var malformed = Path.Combine(directory, "invalid.gspart"); File.WriteAllText(malformed, "{\"Format\":\"Other\",\"Version\":99}");
        try { PartFile.Import(malformed); throw new Exception("Invalid part file accepted"); } catch (InvalidDataException) { }
        var grid = SceneDocument.CreateGrid("空のグリッド"); scene.Canvas.Children.Add(grid);
        var selector = new SceneObject { Kind = ObjectKind.CellSelection }; grid.Children.Add(selector);
        var instance = scene.InsertPart(part, selector);
        if (selector.States.Single().ObjectId != instance.Id) throw new Exception("Part was not auto-assigned to cell state");
        SceneDocument.SetCellState(selector, new(0, 0), selector.States[0].Id);
        var nested = new SceneObject { Kind = ObjectKind.Group }; nested.Children.Add(grid);
        var nestedPart = LibraryPart.FromGroup(nested, "マス選択を含む部品");
        var nestedCopy = nestedPart.CreateCopy();
        var copiedSelector = nestedCopy.Children[0].Children[0];
        if (copiedSelector.States[0].ObjectId != copiedSelector.Children[0].Id || copiedSelector.StateCells[0].StateId != copiedSelector.States[0].Id || copiedSelector.States[0].Id == selector.States[0].Id) throw new Exception("Internal state references were not remapped");
        int count = selector.Children.Count;
        try { scene.InsertPart(nestedPart, selector); throw new Exception("Invalid nested grid accepted as a state template"); } catch (InvalidDataException) { }
        if (selector.Children.Count != count || selector.States.Count != 1) throw new Exception("Failed insert was not atomic");
        part.Template = LibraryPart.FromGroup(new SceneObject { Kind = ObjectKind.Group }, "空").Template;
        if (b.Children.Count != 2 || imported.Template.Children.Count != 2) throw new Exception("Library update changed existing copies");
        scene.Parts.Remove(part); scene.Validate();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        object? Call(string name) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, null);
        Set("document", scene); Set("selectedId", scene.Canvas.Id); Call("RefreshAll");
        var library = (Window)Call("CreatePartLibraryWindow")!; library.Show(); library.UpdateLayout();
        var layout = (DockPanel)library.Content; var actions = layout.Children.OfType<WrapPanel>().Single();
        var original = scene.Snapshot();
        actions.Children.OfType<Button>().Single(button => Equals(button.Content, "コピーを追加")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (scene.Canvas.Children.Count != 3 || scene.Parts.Count != 1) throw new Exception("Library insert UI failed");
        var preview = new RenderTargetBitmap((int)library.ActualWidth, (int)library.ActualHeight, 96, 96, PixelFormats.Pbgra32); preview.Render(library);
        png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(preview));
        using (var file = File.Create(Path.Combine(directory, "part-library-preview.png"))) png.Save(file);
        library.Close(); Call("Undo");
        loaded = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        if (loaded.Snapshot() != original) throw new Exception("Part insert undo failed");
        Set("savedState", loaded.Snapshot());
        Console.WriteLine("PASS: Part copy isolation, image export/import, project persistence, legacy defaults, state reference remapping, atomic insertion, update isolation, library UI and undo");
    }
}
