using System.Windows;
using GridSketch;

internal static class RegionFrameChecks
{
    public static void Run(string directory)
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        var doc = new SceneDocument();
        doc.Canvas.AutoSize = false; doc.Canvas.Width = doc.Canvas.Height = 180; doc.Canvas.Background = "#00000000";
        var grid = SceneDocument.CreateGrid("空のグリッド", 4, 4, 40); grid.X = grid.Y = 20;
        var frame = new SceneObject { Kind = ObjectKind.CellFrame, Thickness = 4, Color = "#80000000" };
        grid.Children.Add(frame); doc.Canvas.Children.Add(grid);
        Check(!SceneDocument.FramedCells(frame, grid).Any(), "New region frame is empty");
        doc.JoinRegionCells(frame, [new(0, 0), new(1, 0)]);
        doc.JoinRegionCells(frame, [new(0, 1), new(1, 1)]);
        Check(SceneDocument.FrameRegions(frame, grid).Count() == 2, "Adjacent rows remain separate regions");
        var geometry = RegionFrameDrawing.Geometry(frame, grid);
        Check(!geometry.FillContains(new Point(40, 20)) && geometry.FillContains(new Point(20, 40)), "Joined cell boundary is absent and separate region boundary is present");
        var bitmap = new SceneRenderer(doc).Export(); var pixels = new byte[180 * 180 * 4]; bitmap.CopyPixels(pixels, 720, 0);
        Check(pixels[((60 * 180) + 40) * 4 + 3] == 128, "Overlapping region boundaries apply alpha once");
        frame.RegionInset = 4;
        geometry = RegionFrameDrawing.Geometry(frame, grid);
        Check(geometry.FillContains(new Point(20, 4)) && !geometry.FillContains(new Point(20, 0))
            && geometry.FillContains(new Point(20, 36)) && geometry.FillContains(new Point(20, 44)), "Positive inset separates adjacent outlines inward");
        frame.RegionInnerStyle = "実線"; frame.RegionInnerThickness = 2;
        geometry = RegionFrameDrawing.Geometry(frame, grid);
        Check(geometry.FillContains(new Point(40, 20)) && !geometry.FillContains(new Point(40, 1)), "Internal lines stay on grid and stop at inset boundary");
        frame.Color = "#80FF0000"; frame.RegionInnerColor = "#800000FF";
        bitmap = new SceneRenderer(doc).Export(); bitmap.CopyPixels(pixels, 720, 0);
        int outsidePixel = (24 * 180 + 40) * 4, insidePixel = (40 * 180 + 60) * 4;
        Check(pixels[outsidePixel + 2] == 128 && pixels[outsidePixel] == 0
            && pixels[insidePixel] == 128 && pixels[insidePixel + 2] == 0,
            "Outer and inner strokes render independent translucent colors");
        var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
        doc.Canvas.Background = "#FFFFFF";
        png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(new SceneRenderer(doc).Export()));
        using (var file = System.IO.File.Create(System.IO.Path.Combine(directory, "region-frame.png"))) png.Save(file);
        PdfExporter.Save(doc, System.IO.Path.Combine(directory, "region-frame.pdf"));
        doc.Canvas.Background = "#00000000";
        frame.RegionInset = -4;
        Check(RegionFrameDrawing.Geometry(frame, grid).FillContains(new Point(20, -4)), "Negative inset expands outside grid boundary");
        doc.SetRegionLink(frame, new(0, 1, false), true);
        Check(SceneDocument.FrameRegions(frame, grid).Count() == 1, "Edge joins existing regions");
        doc.SetFrameCells(frame, [new(1, 1)], false); frame.RegionInset = 4;
        Check(!RegionFrameDrawing.Geometry(frame, grid).FillContains(new Point(60, 60)), "Concave region excludes absent cells");
        doc.Validate();
        var restored = SceneDocument.Restore(doc.Snapshot());
        Check(restored.Find(frame.Id)!.Color == "#80FF0000" && restored.Find(frame.Id)!.RegionInnerColor == "#800000FF",
            "Independent region colors survive serialization");
        Check(restored.Canvas.Children[0].Children[0].RegionLinks.SetEquals(frame.RegionLinks), "Region links survive save and restore");
        var source = SceneDocument.Restore(doc.Snapshot()).Find(frame.Id)!;
        doc.MoveCellContents(frame, source, grid, new Vector(40, 40)); doc.Validate();
        Check(frame.RegionLinks.Contains(new(2, 1, true)), "Region movement shifts links with cells");
        doc.EditGridStructure(grid, true, 2, 1, false); doc.Validate();
        Check(SceneDocument.FrameRegions(frame, grid).Count() == 2, "Inserting blank row separates connections");
        var ring = new SceneObject { Kind = ObjectKind.CellFrame, RegionInset = 4, Thickness = 2 };
        grid.Children.Add(ring);
        doc.JoinRegionCells(ring, Enumerable.Range(0, 3).SelectMany(y => Enumerable.Range(0, 3)
            .Where(x => x != 1 || y != 1).Select(x => new CellPosition(x, y))));
        var ringGeometry = RegionFrameDrawing.Geometry(ring, grid);
        Check(ringGeometry.FillContains(new Point(36, 60)) && ringGeometry.FillContains(new Point(4, 60))
            && !ringGeometry.FillContains(new Point(60, 60)), "Inset ring preserves the hole and moves its inner contour outward");
        Console.WriteLine("PASS: Empty regions, explicit connections, inset signs, internal clipping, concave outlines, opacity, persistence and structural edits");
    }
}
