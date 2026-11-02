using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GridSketch;

internal static class CellSelectionChecks
{
    public static void Run(MainWindow window, string directory)
    {
        var scene = new SceneDocument(); var grid = SceneDocument.CreateGrid("空のグリッド", 3, 1, 40);
        scene.Canvas.Children.Add(grid); scene.Canvas.Padding = 0;
        var selector = new SceneObject { Kind = ObjectKind.CellSelection, Name = "マス選択" }; grid.Children.Add(selector);
        var raw = new byte[80 * 40 * 4];
        for (int i = 0; i < raw.Length; i += 4) { raw[i + 2] = 255; raw[i + 3] = 255; }
        var bitmap = BitmapSource.Create(80, 40, 96, 96, PixelFormats.Bgra32, null, raw, 320);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var imageStream = new MemoryStream(); png.Save(imageStream);
        var red = new SceneObject { Kind = ObjectKind.Image, Name = "赤い画像", ImageData = imageStream.ToArray(), X = 10000, Width = 80, Height = 40 };
        var green = new SceneObject { Kind = ObjectKind.Group, Name = "緑の見本", Children = [new() { Kind = ObjectKind.Shape, FillColor = "#00FF00", Thickness = 0, Width = 40, Height = 40 }] };
        var blue = new SceneObject { Kind = ObjectKind.Shape, Name = "青い図形", FillColor = "#0000FF", Thickness = 0, Width = 40, Height = 40 };
        foreach (var child in new[] { red, green, blue }) { selector.Children.Add(child); SceneDocument.AssignNewState(selector, child); }
        if (selector.States.Count != 3 || selector.StateCells.Count != 0) throw new Exception("Initial states failed");
        var cell = new CellPosition(0, 0);
        foreach (var state in selector.States)
        {
            new GridPaintStroke(scene, selector, grid, new Point(20, 20));
            if (selector.StateCells.Single().StateId != state.Id) throw new Exception("Click cycle failed");
        }
        new GridPaintStroke(scene, selector, grid, new Point(20, 20));
        if (selector.StateCells.Count != 0) throw new Exception("Cycle did not return to empty");
        var drag = new GridPaintStroke(scene, selector, grid, new Point(20, 20)); drag.Move(new Point(100, 20)); drag.Move(new Point(20, 20));
        if (selector.StateCells.Count != 3 || selector.StateCells.Any(c => c.StateId != selector.States[0].Id)) throw new Exception("State drag did not retain first state");
        SceneDocument.SetCellState(selector, new(0, 0), selector.States[^1].Id);
        drag = new GridPaintStroke(scene, selector, grid, new Point(20, 20)); drag.Move(new Point(100, 20)); drag.Move(new Point(20, 20));
        if (selector.StateCells.Count != 0) throw new Exception("Empty state drag did not clear all cells");
        for (int i = 0; i < 3; i++) SceneDocument.SetCellState(selector, new(i, 0), selector.States[i].Id);
        var before = selector.StateCells.Select(c => c.StateId).ToArray();
        selector.States.Reverse();
        if (!selector.StateCells.Select(c => c.StateId).SequenceEqual(before)) throw new Exception("Reorder changed stored states");
        scene.Validate();
        var renderer = new SceneRenderer(scene);
        if (renderer.OutputBounds != new Rect(0, 0, 120, 40) || renderer.Bounds.ContainsKey(red.Id)) throw new Exception("Template leaked into canvas bounds");
        var output = renderer.Export(); var pixels = new byte[120 * 40 * 4]; output.CopyPixels(pixels, 480, 0);
        for (int i = 0; i < 3; i++)
        {
            int offset = (20 * 120 + i * 40 + 20) * 4;
            if (pixels[offset + (2 - i)] != 255 || pixels[offset + ((3 - i) % 3)] != 0) throw new Exception("Assigned template was not rendered at cell center");
        }
        if (pixels[(20 * 120 + 2) * 4] != 255) throw new Exception("Template invaded margin");
        png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(output));
        using (var file = File.Create(Path.Combine(directory, "cell-selection.png"))) png.Save(file);
        scene.Save(Path.Combine(directory, "cell-selection.gridsketch")); PdfExporter.Save(scene, Path.Combine(directory, "cell-selection.pdf"));
        var loaded = SceneDocument.Load(Path.Combine(directory, "cell-selection.gridsketch"));
        if (loaded.Snapshot() != scene.Snapshot()) throw new Exception("State save/load failed");
        try { scene.ResizeGrid(grid, 2, 1, 40, 5); throw new Exception("Resize discarded a state"); } catch (InvalidOperationException) { }
        selector.Children.Remove(red); scene.ClearMissingStateObjects(); scene.Validate();
        if (selector.States.Single(s => s.Id == before[0]).ObjectId is not null || selector.StateCells.Count != 3) throw new Exception("Deleted template left dangling references");
        selector.Children.Add(red); selector.States.Single(s => s.Id == before[0]).ObjectId = red.Id;

        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        void Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        Set("document", scene); Set("selectedId", selector.Id); Call("RefreshAll");
        var panel = (StackPanel)window.FindName("Properties");
        window.UpdateLayout();
        var compactPreview = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); compactPreview.Render(window);
        var previewEncoder = new PngBitmapEncoder(); previewEncoder.Frames.Add(BitmapFrame.Create(compactPreview));
        using (var compactOutput = File.Create(Path.Combine(directory, "compact-states.png"))) previewEncoder.Save(compactOutput);
        var combo = PropertyChecks.Find<ComboBox>(panel, "状態1の表示オブジェクト"); combo.SelectedIndex = 0;
        LivePropertyChecks.Commit(window);
        if (selector.States[0].ObjectId is not null) throw new Exception("Assignment property failed");
        Call("Undo");
        loaded = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        if (loaded.Find(selector.Id)!.States[0].ObjectId != blue.Id) throw new Exception("Assignment undo failed");
        // Reordering and deletion are immediate operations; both must retain stable state identities.
        panel = (StackPanel)window.FindName("Properties");
        var currentSelector = loaded.Find(selector.Id)!;
        window.UpdateLayout();
        var destination = panel.Children.OfType<Border>().ElementAt(1);
        var destinationCombo = PropertyChecks.Find<ComboBox>(destination, "状態2の表示オブジェクト");
        var dragData = new DataObject("GridSketch.State", currentSelector.States[0].Id);
        // WPF constructs these args internally during native drag/drop. Raise the
        // routed preview events from a child control, rather than calling MoveState.
        DragEventArgs DragArgs(RoutedEvent routedEvent)
        {
            var constructor = typeof(DragEventArgs).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Single(c => c.GetParameters().Length == 5);
            var args = (DragEventArgs)constructor.Invoke([dragData, DragDropKeyStates.LeftMouseButton, DragDropEffects.Move, destination, new Point(4, destination.ActualHeight - 1)]);
            args.RoutedEvent = routedEvent; return args;
        }
        var over = DragArgs(DragDrop.PreviewDragOverEvent); destinationCombo.RaiseEvent(over);
        if (!over.Handled || over.Effects != DragDropEffects.Move || destination.BorderThickness.Bottom != 2)
            throw new Exception("State drag-over did not intercept child controls or show insertion line");
        var drop = DragArgs(DragDrop.PreviewDropEvent); destinationCombo.RaiseEvent(drop);
        if (!drop.Handled) throw new Exception("State drop was not accepted over the dropdown");
        if (loaded.Find(selector.Id)!.States[1].ObjectId != blue.Id || loaded.Find(selector.Id)!.StateCells[2].StateId != before[2]) throw new Exception("State reorder UI changed placements");
        panel = (StackPanel)window.FindName("Properties");

        PropertyChecks.Find<Button>(panel, "状態2を削除").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (loaded.Find(selector.Id)!.StateCells.Count != 2 || loaded.Find(selector.Id)!.Children.Count != 3) throw new Exception("State delete did not clear just its placements");
        Call("Undo"); Call("Undo");
        loaded = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        Set("savedState", loaded.Snapshot()); window.UpdateLayout();
        var preview = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); preview.Render(window);
        png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(preview));
        using (var file = File.Create(Path.Combine(directory, "cell-selection-properties.png"))) png.Save(file);
        Console.WriteLine("PASS: Cell selection cycle, drag, stable IDs, image/group rendering, margins, PNG/PDF, persistence, resize safety, missing templates, assignment UI and undo");
    }
}
