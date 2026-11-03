using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GridSketch;

internal static class CellFrameChecks
{
    public static void Run(string directory)
    {
        var scene = new SceneDocument(); scene.Canvas.Background = "#00000000";
        var grid = SceneDocument.CreateGrid("独立マス", 2, 1, 40); grid.Margin = 3; grid.MarginY = 7; scene.Canvas.Children.Add(grid);
        if (!grid.Children.Select(n => n.Kind).SequenceEqual(new[] { ObjectKind.GridText, ObjectKind.CellFrame, ObjectKind.Fill })) throw new Exception("Independent preset order failed");
        var frame = grid.Children[1]; var fill = grid.Children[2]; frame.Thickness = 4; fill.Color = "#FF0000"; fill.FilledCells.Add(new(0, 0));
        if (!fill.UseCellMargin || grid.Children[0].UseCellMargin) throw new Exception("Independent defaults failed");
        scene.Canvas.AutoSize = false; scene.Canvas.Width = 80; scene.Canvas.Height = 40;
        var bitmap = new SceneRenderer(scene).Export(); var pixels = new byte[80 * 40 * 4]; bitmap.CopyPixels(pixels, 80 * 4, 0);
        for (int y = 0; y < 40; y++) for (int x = 0; x < 80; x++)
            if ((x % 40 < 3 || x % 40 >= 37 || y < 7 || y >= 33) && pixels[(y * 80 + x) * 4 + 3] != 0) throw new Exception("Frame/fill invaded margin");
        if (pixels[(15 * 80 + 12) * 4 + 2] != 255) throw new Exception("Inset fill missing");
        var raw = Enumerable.Repeat((byte)255, 100 * 100 * 4).ToArray();
        var source = BitmapSource.Create(100, 100, 96, 96, PixelFormats.Bgra32, null, raw, 400);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source)); using var stream = new MemoryStream(); encoder.Save(stream);
        var image = new SceneObject { Kind = ObjectKind.Image, SnapToCell = true, Column = 1, ImageData = stream.ToArray() }; grid.Children.Insert(0, image);
        var imageBounds = new SceneRenderer(scene).Bounds[image.Id];
        if (imageBounds != new Rect(47, 7, 26, 26)) throw new Exception("Asymmetric image margins failed");
        scene.SetFrameCells(frame, [new(0, 0)], null);
        if (frame.FrameAllCells || frame.FrameCells.Count != 1 || !frame.FrameCells.Contains(new(1, 0))) throw new Exception("Frame materialization/toggle failed");
        grid.Children.Remove(image); fill.FilledCells.Clear(); scene.ResizeGrid(grid, 1, 1, 40, 3, 7);
        if (frame.FrameCells.Count != 0) throw new Exception("Frame crop failed");
        frame.FrameAllCells = true; scene.ResizeGrid(grid, 3, 2, 40, 3, 7);
        if (SceneDocument.FramedCells(frame, grid).Count() != 6) throw new Exception("All-cell resize failed");
        var restored = SceneDocument.Restore(scene.Snapshot()); restored.Validate();
        if (restored.Canvas.Children[0].MarginY != 7 || restored.Canvas.Children[0].Margin != 3) throw new Exception("Separate margins not saved");
        var legacyJson = JsonNode.Parse(scene.Snapshot())!; legacyJson["Canvas"]!["Children"]![0]!.AsObject().Remove("MarginY");
        if (SceneDocument.Restore(legacyJson.ToJsonString()).Canvas.Children[0].MarginY != 3) throw new Exception("Legacy margin fallback failed");
        var sample = new SceneDocument(); var puzzle = SceneDocument.CreateGrid("独立マス", 7, 5, 40); puzzle.Margin = 3; puzzle.MarginY = 2; sample.Canvas.Children.Add(puzzle);
        int[] top = [0, 1, 1, 0, 0, 0, 1], bottom = [4, 4, 3, 4, 3, 4, 3];
        var omitted = Enumerable.Range(0, 7).SelectMany(x => Enumerable.Range(0, 5).Where(y => y < top[x] || y > bottom[x]).Select(y => new CellPosition(x, y)));
        sample.SetFrameCells(puzzle.Children[1], omitted, false);
        foreach (int x in Enumerable.Range(0, 7)) puzzle.Children[2].FilledCells.Add(new(x, 2));
        var output = new PngBitmapEncoder(); output.Frames.Add(BitmapFrame.Create(new SceneRenderer(sample).Export()));
        using (var file = File.Create(Path.Combine(directory, "independent-cells.png"))) output.Save(file);
        sample.Save(Path.Combine(directory, "independent-cells.gridsketch")); PdfExporter.Save(sample, Path.Combine(directory, "independent-cells.pdf"));
        Console.WriteLine("PASS: Independent-cell preset, inset frame/fill/image, toggling, resize and legacy margins");
    }
}
