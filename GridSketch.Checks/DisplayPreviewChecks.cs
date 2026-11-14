using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GridSketch;

internal static class DisplayPreviewChecks
{
    public static void Run(MainWindow window, string directory)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Call(string method, params object[] args) => typeof(MainWindow).GetMethod(method, flags)!.Invoke(window, args);
        void Set(string field, object value) => typeof(MainWindow).GetField(field, flags)!.SetValue(window, value);
        var doc = new SceneDocument(); doc.Canvas.AutoSize = false; doc.Canvas.Width = 420; doc.Canvas.Height = 250;
        doc.PdfExport = PdfSettings.ForPreset("印刷会社用");
        foreach (var (color, x) in new[] { ("#FF0000", 20), ("#00FF00", 150), ("#0000FF", 280) })
            doc.Canvas.Children.Add(new SceneObject { Kind = ObjectKind.Shape, X = x, Y = 60, Width = 100, Height = 100, FillColor = color });
        Set("document", doc); Set("selectedId", doc.Canvas.Children[0].Id); Call("RefreshAll");
        var snapshot = doc.Snapshot();
        var surface = (SceneSurface)window.FindName("Surface");
        byte[] Pixels(BitmapSource bitmap) { var result = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(result, bitmap.PixelWidth * 4, 0); return result; }
        var original = Pixels(surface.Renderer!.Export());
        Call("DisplayPreviewClick", window, new RoutedEventArgs());
        var dialog = (Window)typeof(MainWindow).GetField("displayPreviewWindow", flags)!.GetValue(window)!;
        if (!dialog.IsVisible || !window.IsEnabled || surface.HasDisplayPreview) throw new Exception("Preview must open modeless in normal mode");
        Call("DisplayPreviewClick", window, new RoutedEventArgs());
        if (!ReferenceEquals(dialog, typeof(MainWindow).GetField("displayPreviewWindow", flags)!.GetValue(window))) throw new Exception("Preview window duplicated");
        PropertyChecks.Find<RadioButton>(dialog, "色覚：2型（D型）").IsChecked = true;
        if (surface.VisionPreview != ColorVisionPreview.Deutan) throw new Exception("Vision preview not applied");
        var cmyk = PropertyChecks.Find<CheckBox>(dialog, "CMYKプレビュー");
        if (doc.PdfExport.ProfileData is not null) cmyk.IsChecked = true;
        window.UpdateLayout();
        var image = new RenderTargetBitmap((int)surface.ActualWidth, (int)surface.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(surface);
        if (typeof(SceneSurface).GetField("displayBitmap", flags)!.GetValue(surface) is not BitmapSource transformed) throw new Exception("Preview bitmap not rendered");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(transformed));
        using (var output = File.Create(Path.Combine(directory, "display-preview.png"))) encoder.Save(output);
        dialog.UpdateLayout();
        image = new RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(dialog);
        encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using (var output = File.Create(Path.Combine(directory, "display-preview-dialog.png"))) encoder.Save(output);
        if (doc.Snapshot() != snapshot || !original.SequenceEqual(Pixels(surface.Renderer.Export()))) throw new Exception("Preview modified document or PNG export");
        // Changing documents must not keep the previous tab's ICC conversion.
        var other = SceneDocument.Restore(snapshot); other.PdfExport.ProfileData = null;
        Set("document", other); Set("selectedId", other.Canvas.Id); Call("RefreshAll");
        if (cmyk.IsChecked == true || cmyk.IsEnabled) throw new Exception("Missing ICC retained previous tab's proof");
        dialog.Close();
        if (surface.HasDisplayPreview) throw new Exception("Closing preview did not restore normal rendering");
        Call("DisplayPreviewClick", window, new RoutedEventArgs());
        dialog = (Window)typeof(MainWindow).GetField("displayPreviewWindow", flags)!.GetValue(window)!;
        if (surface.HasDisplayPreview || !PropertyChecks.Find<RadioButton>(dialog, "色覚：なし").IsChecked!.Value) throw new Exception("Reopening did not reset preview");
        dialog.Close();
        foreach (var mode in new[] { ColorVisionPreview.Protan, ColorVisionPreview.Deutan, ColorVisionPreview.Tritan })
        {
            byte[] pixels = [255,255,255,128, 0,0,0,255, 0,0,255,100];
            DisplayPreview.Simulate(pixels, mode);
            if (pixels[0] != 255 || pixels[1] != 255 || pixels[2] != 255 || pixels[3] != 128 || pixels[4] != 0 || pixels[11] != 100 || pixels[10] == 255 && pixels[9] == 0 && pixels[8] == 0)
                throw new Exception("Vision transform neutrality, alpha or color failed");
        }
        Console.WriteLine("PASS: Display preview modeless lifecycle, ICC/vision combination, tab profile, close/reset and export isolation");
    }
}
