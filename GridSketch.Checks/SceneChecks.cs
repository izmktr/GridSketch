using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GridSketch;

internal static class SceneChecks
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
    static Color Pixel(BitmapSource source, int x, int y)
    {
        var pixel = new byte[4]; source.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0); return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }
    public static void Run(string directory)
    {
        var scene = new SceneDocument(); var grid = SceneDocument.CreateGrid("クロスワード", 5, 4, 48); scene.Canvas.Children.Add(grid);
        var resizeScene = SceneDocument.Migrate(new Document());
        var resizeGrid = resizeScene.Canvas.Children[0];
        resizeScene.ResizeGrid(resizeGrid, 4, 3, 32, 5); resizeScene.Validate();
        Check(resizeGrid.Children.Where(n => n.Kind == ObjectKind.Border).SelectMany(n => n.Edges).All(e => e.IsInside(4, 3)) &&
            resizeGrid.Children.Single(n => n.Name == "外枠・縦").Edges.Contains(new(4, 2, true)), "Legacy default grid shrinks without deleting borders");
        resizeScene.ResizeGrid(resizeGrid, 8, 7, 48, 5); resizeScene.Validate();
        Check(resizeGrid.Children.Single(n => n.Name == "外枠・縦").Edges.Count == 14 && resizeGrid.Children.Single(n => n.Name == "外枠・横").Edges.Count == 16, "Explicit outer borders grow to full new perimeter");
        var custom = new SceneObject { Kind = ObjectKind.Border, Edges = [new(1, 1, true), new(6, 5, true), new(8, 1, true)] };
        resizeGrid.Children.Add(custom); resizeScene.ResizeGrid(resizeGrid, 4, 3, 48, 5); resizeScene.Validate();
        Check(custom.Edges.SetEquals([new(1, 1, true), new(4, 1, true)]), "Custom edges crop while surviving boundary edges follow frame");
        var characters = resizeGrid.Children.Single(n => n.Kind == ObjectKind.GridText); characters.Glyphs.Add(new Glyph { Column = 3, Row = 2, Text = "字" });
        var beforeResize = resizeScene.Snapshot(); bool resizeRejected = false;
        try { resizeScene.ResizeGrid(resizeGrid, 2, 2, 48, 5); } catch (InvalidOperationException ex) { resizeRejected = ex.Message.Contains(characters.Name); }
        Check(resizeRejected && resizeScene.Snapshot() == beforeResize, "Out-of-range text is identified and resize leaves document unchanged");
        var fresh = new SceneDocument(); var freshGrid = SceneDocument.CreateGrid("基本の方眼紙"); fresh.Canvas.Children.Add(freshGrid);
        fresh.ResizeGrid(freshGrid, 1, 1, 24, 5); fresh.Validate();
        Check(SceneDocument.BorderEdges(freshGrid.Children[0], freshGrid).Count() == 4, "Fresh default grid resizes immediately to one cell");
        var text = grid.Children.Single(n => n.Kind == ObjectKind.GridText); var fill = grid.Children.Single(n => n.Kind == ObjectKind.Fill);
        var plan = scene.PlanText(text, 0, 0, "さくら", false); text.Glyphs.AddRange(plan);
        Check(text.Glyphs.Count == 3 && text.Glyphs[2].Column == 2 && grid.Children.Count == 4, "Text input remains one layer");
        Check(scene.PlanText(text, 3, 0, "天地", true)[1].Row == 1, "Vertical bulk text placement");
        fill.FilledCells.Add(new(3, 1)); fill.Visible = false;
        bool rejected = false; try { scene.PlanText(text, 3, 0, "天地", true); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && text.Glyphs.Count == 3, "Black cells block text atomically even when hidden");
        rejected = false; try { scene.PlanText(text, 4, 0, "二字", false); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Out-of-bounds bulk placement rejected");
        Check(scene.PlanText(text, 0, 1, "か\u3099", false).Count == 1, "Combining character treated as one grid glyph");
        var outer = grid.Children[0]; Check(SceneDocument.BorderEdges(outer, grid).Count() == 18, "Outer border preset edge count");
        grid.Columns = 6; Check(SceneDocument.BorderEdges(outer, grid).Count() == 20, "Preset follows resized grid");
        scene.ToggleEdge(outer, new GridEdge(0, 0, false));
        Check(outer.Pattern == "個別" && !outer.Edges.Contains(new(0, 0, false)), "Editing a preset materializes independent edges");
        var individual = new SceneObject { Kind = ObjectKind.Border, Thickness = 10 }; grid.Children.Insert(0, individual);
        var bounds = new SceneRenderer(scene).OutputBounds; scene.ToggleEdge(individual, new(1, 1, true));
        Check(new SceneRenderer(scene).OutputBounds == bounds, "First individual edge does not shift auto canvas");
        scene.ToggleEdge(individual, new(1, 1, true)); Check(new SceneRenderer(scene).OutputBounds == bounds, "Last individual edge removal keeps canvas stable");
        grid.X = 100; grid.Y = 50; grid.Angle = 90;
        var surface = new SceneSurface(); surface.Refresh(scene);
        var world = scene.World(grid).Transform(new Point(24, 72));
        Check(surface.CellAt(grid, new Point(world.X + SceneSurface.Origin, world.Y + SceneSurface.Origin)) == new CellPosition(0, 1), "Rotated-grid cell hit testing");
        var edgePoint = scene.World(grid).Transform(new Point(48, 24));
        Check(surface.EdgeAt(grid, new Point(edgePoint.X + SceneSurface.Origin, edgePoint.Y + SceneSurface.Origin), 1) == new GridEdge(1, 0, true), "Rotated-grid shared-edge hit testing");
        var shape = new SceneObject { Kind = ObjectKind.Shape, X = 20, Y = 30, Angle = 15, Width = 40, Height = 20, FillColor = "#FF0000", Thickness = 0 };
        grid.Children.Insert(0, shape);
        var group = new SceneObject { Kind = ObjectKind.Group, X = 60, Y = -20, Angle = -30 }; scene.Canvas.Children.Add(group);
        var before = scene.World(shape).Transform(new Point(5, 6)); scene.Reparent(shape, group); var after = scene.World(shape).Transform(new Point(5, 6));
        Check((before - after).Length < 0.00001, "Reparent preserves world position and rotation");
        rejected = false; try { scene.Reparent(group, shape); } catch (InvalidOperationException) { rejected = true; } Check(rejected, "Invalid parent rejected");
        var path = Path.Combine(directory, "scene-roundtrip.gridsketch"); scene.Save(path); var restored = SceneDocument.Load(path);
        Check(restored.Find(text.Id)!.Glyphs.Count == 3 && restored.Find(outer.Id)!.Pattern == "個別", "Version 3 hierarchy and content round-trip");
        var paint = new SceneDocument(); paint.Canvas.AutoSize = false; paint.Canvas.Width = paint.Canvas.Height = 100; paint.Canvas.Background = "#00000000";
        paint.Canvas.Children = [new() { Kind = ObjectKind.Shape, FillColor = "#FF0000", Width = 40, Height = 40, Thickness = 0 }, new() { Kind = ObjectKind.Shape, FillColor = "#0000FF", Width = 80, Height = 80, Thickness = 0 }];
        var output = new SceneRenderer(paint).Export(); Check(Pixel(output, 20, 20).R == 255 && Pixel(output, 60, 60).B == 255, "Top layer paints in front");
        Check(Pixel(output, 95, 95).A == 0, "Transparent canvas preserved");
        paint.Canvas.Dpi = 300; var dpiOutput = new SceneRenderer(paint).Export();
        Check(dpiOutput.PixelWidth == output.PixelWidth && dpiOutput.DpiX == 300, "DPI changes metadata without resampling");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(dpiOutput)); using var stream = new MemoryStream(); encoder.Save(stream);
        var decoded = Document.DecodeImage(stream.ToArray()); Check(Math.Abs(decoded.DpiX - 300) < 0.1 && decoded.PixelWidth == 100, "PNG retains DPI and dimensions");
        Check(new SceneRenderer(paint).Export(2).PixelWidth == 200, "Explicit export scale doubles pixels");
        paint.Canvas.AutoSize = true; paint.Canvas.Padding = 0; paint.Canvas.Children[0].Visible = false; paint.Canvas.Children[1].Angle = 45;
        var rotated = new SceneRenderer(paint); Check(rotated.OutputBounds.Width > 110 && rotated.OutputBounds.X < 0, "Auto canvas includes negative rotated bounds");
        var old = new Document { Columns = 2, Rows = 2, CellSize = 64 };
        old.Cells.Add(new Cell { Column = 1, Row = 0, Text = "あ", FontSize = 19 }); old.Walls.Add(new(0, 0, true));
        var migrated = SceneDocument.Migrate(old); migrated.Validate();
        Check(migrated.All().Single(n => n.Kind == ObjectKind.GridText).Glyphs[0].Size == 19 && migrated.Canvas.Children[0].Children[0].Edges.Count == 1, "Legacy migration preserves per-cell font size and walls");
        var oldSurface = new GridSurface { Document = old }; oldSurface.Refresh();
        Check(new SceneRenderer(migrated).Export().PixelWidth == oldSurface.Export(1).PixelWidth, "Legacy migration preserves output dimensions");
        old.Strokes[2].Kind = "破線"; old.Strokes[3].Kind = "点線";
        oldSurface.Refresh();
        var legacyImage = oldSurface.Export(1); var migratedImage = new SceneRenderer(SceneDocument.Migrate(old)).Export();
        var legacyPixels = new byte[legacyImage.PixelWidth * legacyImage.PixelHeight * 4]; var migratedPixels = new byte[legacyPixels.Length];
        legacyImage.CopyPixels(legacyPixels, legacyImage.PixelWidth * 4, 0); migratedImage.CopyPixels(migratedPixels, migratedImage.PixelWidth * 4, 0);
        Check(legacyPixels.Zip(migratedPixels, (a, b) => Math.Abs(a - b)).Max() <= 1, "Migration preserves solid/dashed/dotted lines and glyph pixels");
        grid.X = -5000; surface.Refresh(scene);
        var negativeWorld = scene.World(grid).Transform(new Point(24, 24));
        Check(surface.OriginX > 5000 && surface.CellAt(grid, new Point(negativeWorld.X + surface.OriginX, negativeWorld.Y + surface.OriginY)) == new CellPosition(0, 0), "Large negative coordinates stay reachable on workspace");
        var imageScene = new SceneDocument(); var imageGrid = SceneDocument.CreateGrid("空のグリッド", 1, 1, 64); imageScene.Canvas.Children.Add(imageGrid);
        imageScene.Canvas.AutoSize = false; imageScene.Canvas.Width = imageScene.Canvas.Height = 64;
        var raw = Enumerable.Repeat(new byte[] { 0, 0, 255, 255 }, 100 * 50).SelectMany(p => p).ToArray();
        var imageEncoder = new PngBitmapEncoder(); imageEncoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(100, 50, 96, 96, PixelFormats.Bgra32, null, raw, 400)));
        using var imageData = new MemoryStream(); imageEncoder.Save(imageData); imageGrid.Margin = 8;
        imageGrid.Children.Add(new SceneObject { Kind = ObjectKind.Image, SnapToCell = true, ImageData = imageData.ToArray() });
        var fitted = new SceneRenderer(imageScene).Export();
        Check(Pixel(fitted, 8, 20).R == 255 && Pixel(fitted, 8, 20).G == 0 && Pixel(fitted, 7, 20).G == 255 && Pixel(fitted, 55, 43).G == 0 && Pixel(fitted, 56, 43).G == 255, "Scene image placement preserves margin and aspect ratio");
        var legacyPath = Path.Combine(directory, "legacy.gridsketch"); old.Save(legacyPath);
        Check(SceneDocument.Load(legacyPath).Version == 3 && Document.Load(legacyPath).Version == 2, "Loading legacy projects does not overwrite them");
        // A useful sample project exercises all primary drawing types.
        var sample = new SceneDocument(); var sampleGrid = SceneDocument.CreateGrid("クロスワード", 9, 9, 56); sampleGrid.Y = 70; sample.Canvas.Children.Add(sampleGrid);
        var sampleText = sampleGrid.Children.Single(n => n.Kind == ObjectKind.GridText);
        sampleText.Glyphs.AddRange(sample.PlanText(sampleText, 1, 1, "グリッド", false));
        sampleText.Glyphs.AddRange(sample.PlanText(sampleText, 6, 2, "ことば", true));
        var sampleFill = sampleGrid.Children.Single(n => n.Kind == ObjectKind.Fill); sampleFill.FilledCells.UnionWith([new(0, 0), new(8, 0), new(4, 4), new(0, 8), new(8, 8)]);
        sampleGrid.Children.Insert(0, new SceneObject { Kind = ObjectKind.Line, Name = "曲線の矢印", X = 84, Y = 308, EndX = 224, EndY = 56, ControlX = 120, ControlY = -90, PathKind = "曲線", Color = "#E16B35", Thickness = 4 });
        sample.Canvas.Children.Insert(0, new SceneObject { Kind = ObjectKind.Text, Name = "タイトル", Text = "GridSketch — オブジェクト編集", FontSize = 26, Color = "#162A40" });
        sample.Validate(); sample.Save(Path.Combine(directory, "objects-sample.gridsketch"));
        var samplePng = new PngBitmapEncoder(); samplePng.Frames.Add(BitmapFrame.Create(new SceneRenderer(sample).Export())); using var sampleFile = File.Create(Path.Combine(directory, "objects-sample.png")); samplePng.Save(sampleFile);
        Console.WriteLine("PASS: Sample scene exported");
    }
}
