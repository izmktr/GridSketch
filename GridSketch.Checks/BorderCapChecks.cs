using System.IO;
using System.Windows.Media.Imaging;
using GridSketch;

internal static class BorderCapChecks
{
    public static void Run(string directory)
    {
        var scene = new SceneDocument(); scene.Canvas.AutoSize = false; scene.Canvas.Width = scene.Canvas.Height = 160; scene.Canvas.Background = "#00000000";
        var grid = SceneDocument.CreateGrid("空のグリッド", 4, 4, 40); scene.Canvas.Children.Add(grid);
        var border = new SceneObject { Kind = ObjectKind.Border, Thickness = 16, Color = "#FF0000", Edges = [new(1, 1, false), new(2, 1, true)] }; grid.Children.Add(border);
        if (border.EffectiveBorderEndCap != "四角") throw new Exception("New border is not square capped");
        foreach (var cap in new[] { "中央まで", "四角", "丸" })
        {
            border.BorderEndCap = cap; scene.Validate();
            var bitmap = new SceneRenderer(scene).Export(); var pixels = new byte[160 * 160 * 4]; bitmap.CopyPixels(pixels, 640, 0);
            byte Alpha(int x, int y) => pixels[(y * 160 + x) * 4 + 3];
            if (cap == "中央まで" && (Alpha(84, 36) != 0 || Alpha(34, 40) != 0)) throw new Exception("Flat ends extend beyond grid point");
            if (cap == "四角" && (Alpha(86, 34) != 255 || Alpha(34, 34) != 255)) throw new Exception("Square caps leave missing corners");
            if (cap == "丸" && (Alpha(84, 36) != 255 || Alpha(86, 33) != 0 || Alpha(34, 40) != 255)) throw new Exception("Round cap does not match half-width radius");
            var restored = SceneDocument.Restore(scene.Snapshot()); restored.Validate();
            if (restored.Canvas.Children[0].Children[0].EffectiveBorderEndCap != cap) throw new Exception("Cap setting lost on save");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(Path.Combine(directory, "border-cap-" + cap + ".png"))) encoder.Save(output);
            PdfExporter.Save(scene, Path.Combine(directory, "border-cap-" + cap + ".pdf"));
        }
        border.BorderEndCap = null; border.SquareCaps = false;
        var legacy = SceneDocument.Restore(scene.Snapshot());
        if (legacy.Canvas.Children[0].Children[0].EffectiveBorderEndCap != "中央まで") throw new Exception("Legacy flat cap changed");
        foreach (var preset in new[] { "基本の方眼紙", "クロスワード", "将棋盤" })
            if (SceneDocument.CreateGrid(preset).Children.Where(n => n.Kind == ObjectKind.Border).Any(n => n.EffectiveBorderEndCap != "四角")) throw new Exception("Preset cap default failed");
        Console.WriteLine("PASS: Border flat/square/round pixel geometry, new defaults, legacy preservation, persistence and PNG/PDF exports");
    }
}
