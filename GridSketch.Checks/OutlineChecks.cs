using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using GridSketch;

internal static class OutlineChecks
{
    public static void Run(string directory)
    {
        var scene = new SceneDocument(); scene.Canvas.Background = "#00000000";
        var text = new SceneObject { Kind = ObjectKind.Text, Text = "縁取り ABC", FontSize = 48, Color = "#800000FF" };
        scene.Canvas.Children.Add(text);
        var original = new SceneRenderer(scene).OutputBounds;
        text.Style = new LayerStyle { OutlineEnabled = true, OutlineWidth = 5, OutlineColor = "#FFFF8800" };
        var renderer = new SceneRenderer(scene);
        if (renderer.OutputBounds.Width < original.Width + 8 || renderer.OutputBounds.Height < original.Height + 8) throw new Exception("Auto canvas did not include outline");
        var snapshot = SceneDocument.Restore(scene.Snapshot());
        if (!snapshot.Canvas.Children[0].Style.OutlineEnabled || snapshot.Canvas.Children[0].Style.OutlineWidth != 5) throw new Exception("Layer style not preserved");
        var bitmap = renderer.Export(); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(directory, "outline-preview.png"))) encoder.Save(file);
        // Compare a translucent glyph with/without its outer ring at interior pixels.
        text.Text = "■";
        scene.Canvas.AutoSize = false; scene.Canvas.Width = 400; scene.Canvas.Height = 100; scene.Canvas.X = -10; scene.Canvas.Y = -10;
        var styled = new SceneRenderer(scene).Export(); text.Style.OutlineEnabled = false; var plain = new SceneRenderer(scene).Export();
        byte[] a = new byte[400 * 100 * 4], b = new byte[a.Length]; styled.CopyPixels(a, 400 * 4, 0); plain.CopyPixels(b, 400 * 4, 0);
        int interior = 0, ring = 0;
        for (int i = 0; i < a.Length; i += 4)
        {
            int x = i / 4 % 400, y = i / 4 / 400;
            bool deepInterior = x > 0 && x < 399 && y > 0 && y < 99 && b[i + 3] == 128 && b[i] == 128;
            if (deepInterior) for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) deepInterior &= b[i + (dy * 400 + dx) * 4 + 3] == 128;
            if (deepInterior) { interior++; if (a[i + 3] != 128 || a[i] != 128 || a[i + 2] != 0) throw new Exception("Outline leaked beneath translucent text"); }
            if (b[i + 3] == 0 && a[i + 3] > 200) ring++;
        }
        if (interior < 20 || ring < 20) throw new Exception("Outline/interior pixels missing");
        var gridScene = new SceneDocument(); gridScene.Canvas.AutoSize = false; gridScene.Canvas.Width = 96; gridScene.Canvas.Height = 48; gridScene.Canvas.Background = "#00000000";
        var grid = SceneDocument.CreateGrid("空のグリッド", 2, 1, 48); grid.Children.Clear(); gridScene.Canvas.Children.Add(grid);
        var glyph = new SceneObject { Kind = ObjectKind.GridText, FontSize = 180, Glyphs = [new Glyph { Column = 0, Row = 0, Text = "縁" }], Style = new LayerStyle { OutlineEnabled = true, OutlineWidth = 20, OutlineColor = "#FF0000" } }; grid.Children.Add(glyph);
        foreach (var alignment in new[] { "中央", "左上" })
        {
            glyph.Alignment = alignment; gridScene.Validate(); var output = new SceneRenderer(gridScene).Export(); var pixels = new byte[96 * 48 * 4]; output.CopyPixels(pixels, 96 * 4, 0);
            for (int y = 0; y < 48; y++) for (int x = 0; x < 96; x++)
                if ((x == 0 || x >= 47 || y == 0 || y == 47) && pixels[(y * 96 + x) * 4 + 3] != 0) throw new Exception("Outlined glyph did not fit inside cell");
        }
        Console.WriteLine("PASS: Text outline persistence, auto bounds, transparent interiors and grid fitting");
    }
}
