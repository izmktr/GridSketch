using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using GridSketch;

internal static class CanvasChecks
{
    public static void Run(MainWindow window, string directory)
    {
        var scene = new SceneDocument(); scene.Canvas.Children.Add(SceneDocument.CreateGrid("基本の方眼紙"));
        var before = new SceneRenderer(scene).OutputBounds;
        var position = (scene.Canvas.Children[0].X, scene.Canvas.Children[0].Y);
        var panel = new StackPanel();
        var apply = (Action)typeof(MainWindow).GetMethod("CanvasSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [panel, scene.Canvas, before])!;
        var mode = panel.Children.OfType<ComboBox>().Single(); mode.SelectedIndex = 1; apply(); scene.Validate();
        if (new SceneRenderer(scene).OutputBounds != before) throw new Exception("Auto to fixed changed output bounds");
        var fixedPanel = panel.Children.OfType<StackPanel>().Last();
        var unit = fixedPanel.Children.OfType<ComboBox>().First(); unit.SelectedItem = "mm"; apply();
        if (new SceneRenderer(scene).OutputBounds != before) throw new Exception("Unit switch changed pixel bounds");
        fixedPanel.Children.OfType<ComboBox>().Last().SelectedIndex = 2;
        panel.Children.OfType<TextBox>().Single().Text = "300"; apply(); scene.Validate();
        if (scene.Canvas.Width != 210 || scene.Canvas.Height != 297 || scene.Canvas.CanvasUnit != "mm") throw new Exception("A4 preset failed");
        var output = new SceneRenderer(scene).Export(1);
        if (output.PixelWidth != 2480 || output.PixelHeight != 3508 || output.DpiX != 300) throw new Exception("A4 PNG dimensions failed");
        scene.Canvas.Dpi = 150;
        var low = new SceneRenderer(scene).OutputBounds;
        if (low.Width != 1240 || low.Height != 1754) throw new Exception("mm DPI conversion failed");
        if ((scene.Canvas.Children[0].X, scene.Canvas.Children[0].Y) != position) throw new Exception("Canvas edit moved content");
        var file = Path.Combine(directory, "canvas-mm.gridsketch"); scene.Save(file);
        var loaded = SceneDocument.Load(file);
        if (loaded.Canvas.CanvasUnit != "mm" || new SceneRenderer(loaded).OutputBounds != low) throw new Exception("Canvas unit round trip failed");
        mode.SelectedIndex = 0; apply();
        if (new SceneRenderer(scene).OutputBounds != before) throw new Exception("Fixed to auto did not fit content");
        Console.WriteLine("PASS: Canvas auto/fixed switching, unit conversion, A4 PNG, DPI and save/load");
    }
}
