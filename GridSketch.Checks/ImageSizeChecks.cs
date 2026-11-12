using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GridSketch;

internal static class ImageSizeChecks
{
    public static void Run(MainWindow window)
    {
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(80, 40, 96, 96, PixelFormats.Bgra32, null,
            Enumerable.Repeat((byte)255, 80 * 40 * 4).ToArray(), 320)));
        using var stream = new MemoryStream(); encoder.Save(stream);
        var scene = new SceneDocument();
        var image = new SceneObject { Kind = ObjectKind.Image, ImageData = stream.ToArray() };
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        Set("document", scene);
        Call("InitializeObjectLayout", image, scene.Canvas); scene.Canvas.Children.Add(image);
        Check(image.Width == 80 && image.Height == 40, "New images must use original pixel dimensions");
        Set("selectedId", image.Id); Call("RefreshAll");
        var panel = (StackPanel)window.FindName("Properties");
        var width = PropertyChecks.Find<TextBox>(panel, "幅（px）");
        var height = PropertyChecks.Find<TextBox>(panel, "高さ（px）");
        var keep = PropertyChecks.Find<CheckBox>(panel, "縦横比を保存");
        width.Text = "160"; LivePropertyChecks.Commit(window);
        Check(image.Width == 160 && image.Height == 80 && height.Text == "80", "Width must update height at the locked ratio");
        height.Text = "60"; LivePropertyChecks.Commit(window);
        Check(image.Width == 120 && image.Height == 60 && width.Text == "120", "Height must update width at the locked ratio");
        keep.IsChecked = false; keep.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        width.Text = "240"; LivePropertyChecks.Commit(window);
        Check(image.Width == 240 && image.Height == 60 && SceneRenderer.ImagePlacementRect(image, null) == new Rect(0, 0, 240, 60),
            "Unlocked dimensions must stretch the image itself");
        Check(SceneDocument.Restore(scene.Snapshot()).Find(image.Id)!.KeepImageAspectRatio == false, "Aspect checkbox must persist");
        Call("Undo");
        var restored = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        Check(restored.Find(image.Id)!.Width == 120, "Image size edits must undo");
        Set("savedState", restored.Snapshot());
        Console.WriteLine("PASS: Actual image dimensions, original size, linked width/height, stretching, persistence and undo");
    }
}
