using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GridSketch;

internal static class PropertyChecks
{
    public static T Find<T>(DependencyObject root, string name) where T : DependencyObject
    {
        IEnumerable<DependencyObject> Walk(DependencyObject node)
        {
            yield return node;
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
                foreach (var descendant in Walk(child)) yield return descendant;
        }
        return Walk(root).OfType<T>().Single(n => AutomationProperties.GetName(n) == name);
    }

    public static void Run(MainWindow window, string directory)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var scene = new SceneDocument();
        var shape = new SceneObject { Kind = ObjectKind.Shape, Name = "サイズの編集", Width = 1250.25, Height = 80, X = -12.5, Y = 30 };
        scene.Canvas.Children.Add(shape);
        typeof(MainWindow).GetField("document", flags)!.SetValue(window, scene);
        typeof(MainWindow).GetField("selectedId", flags)!.SetValue(window, shape.Id);
        typeof(MainWindow).GetMethod("RefreshAll", flags)!.Invoke(window, null);
        window.UpdateLayout();
        var panel = (StackPanel)window.FindName("Properties");
        var width = Find<TextBox>(panel, "幅（px）"); var height = Find<TextBox>(panel, "高さ（px）");
        var slider = Find<Slider>(panel, "幅（px） スライダー");
        if (double.Parse(width.Text) != 1250.25) throw new Exception("Slider clamped original size");
        var a = width.TranslatePoint(new Point(), panel); var b = height.TranslatePoint(new Point(), panel);
        if (Math.Abs(a.Y - b.Y) > 1 || b.X <= a.X) throw new Exception("Size fields are not side by side");
        width.Text = "72.25";
        if (Math.Abs(slider.Value - Math.Log2(72.25)) > 1e-10 || double.Parse(width.Text) != 72.25) throw new Exception("Precise numeric input did not update log slider");
        width.Text = "-";
        if (width.Text != "-") throw new Exception("Intermediate numeric input lost");
        slider.Value = Math.Log2(95);
        if (double.Parse(width.Text) != 96 || shape.Width != 96) throw new Exception("Log slider snapping or deferred apply failed");
        foreach (double number in new[] { 12d, 16, 20, 24, 32, 40, 48, 64, 80, 96, 128 })
        {
            slider.Value = Math.Log2(number);
            if (double.Parse(width.Text) != number) throw new Exception("Missing preferred size: " + number);
        }
        width.Text = "23";
        Slider.IncreaseSmall.Execute(null, slider);
        if (double.Parse(width.Text) != 24) throw new Exception("Keyboard did not advance to next preferred size");
        Slider.DecreaseSmall.Execute(null, slider);
        if (double.Parse(width.Text) != 20) throw new Exception("Keyboard did not return to previous preferred size");
        var stroke = Find<TextBox>(panel, "線の太さ");
        var strokeSlider = Find<Slider>(panel, "線の太さ スライダー");
        foreach (double number in new[] { 0d, 0.25, 0.5, 0.75, 1, 1.5, 2.5, 64 })
        {
            strokeSlider.Value = Math.Log2(number + 0.25);
            if (double.Parse(stroke.Text) != number) throw new Exception("Missing preferred stroke width: " + number);
        }
        stroke.Text = "1.2";
        if (double.Parse(stroke.Text) != 1.2 || Math.Abs(strokeSlider.Value - Math.Log2(1.45)) > 1e-10) throw new Exception("Exact stroke width was rounded");
        width.Text = "1500.5";
        LivePropertyChecks.Commit(window);
        if (shape.Width != 1500.5 || shape.Height != 80 || shape.X != -12.5) throw new Exception("Paired property apply failed");
        typeof(MainWindow).GetMethod("Undo", flags)!.Invoke(window, null);
        var restored = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        if (restored.Canvas.Children[0].Width != 20) throw new Exception("Size edit undo failed");
        // This temporary document needs no save prompt when the test window closes.
        typeof(MainWindow).GetField("savedState", flags)!.SetValue(window, restored.Snapshot());
        window.UpdateLayout();
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(Path.Combine(directory, "properties-preview.png")); encoder.Save(stream);
        Console.WriteLine("PASS: Paired property layout, logarithmic snapping, keyboard steps, zero/fractional strokes, precise/out-of-range input, apply and undo");
    }
}
