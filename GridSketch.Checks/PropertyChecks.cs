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
        if (slider.Value != 72.25) throw new Exception("Numeric input did not update slider");
        width.Text = "-";
        if (width.Text != "-") throw new Exception("Intermediate numeric input lost");
        slider.Value = 240;
        if (double.Parse(width.Text) != 240 || shape.Width != 1250.25) throw new Exception("Slider synchronization or deferred apply failed");
        width.Text = "1500.5";
        panel.Children.OfType<Button>().Single(b => Equals(b.Content, "プロパティを適用")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (shape.Width != 1500.5 || shape.Height != 80 || shape.X != -12.5) throw new Exception("Paired property apply failed");
        typeof(MainWindow).GetMethod("Undo", flags)!.Invoke(window, null);
        var restored = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        if (restored.Canvas.Children[0].Width != 1250.25) throw new Exception("Size edit undo failed");
        // This temporary document needs no save prompt when the test window closes.
        typeof(MainWindow).GetField("savedState", flags)!.SetValue(window, restored.Snapshot());
        window.UpdateLayout();
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(Path.Combine(directory, "properties-preview.png")); encoder.Save(stream);
        Console.WriteLine("PASS: Paired property layout, slider synchronization, precise/out-of-range input, apply and undo");
    }
}
