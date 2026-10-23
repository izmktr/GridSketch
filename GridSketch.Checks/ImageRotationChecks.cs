using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GridSketch;

internal static class ImageRotationChecks
{
    public static void Run()
    {
        var scene = new SceneDocument();
        var image = new SceneObject { Kind = ObjectKind.Image, Width = 100, Height = 60, X = 25, Y = 35 };
        var bitmap = BitmapSource.Create(10, 6, 96, 96, PixelFormats.Bgra32, null, Enumerable.Repeat((byte)255, 240).ToArray(), 40);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(); encoder.Save(stream); image.ImageData = stream.ToArray();
        scene.Canvas.Children.Add(image);
        var center = new Point(50, 30);
        foreach (double angle in new[] { 0d, 45, 90, 180, -90 })
        {
            image.Angle = angle;
            var actual = scene.World(image).Transform(center);
            if ((actual - new Point(75, 65)).Length > 1e-8) throw new Exception("Image center moved during rotation");
            var renderer = new SceneRenderer(scene);
            var bounds = renderer.Bounds[image.Id];
            if ((new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2) - actual).Length > 1e-8) throw new Exception("Rendered image has wrong pivot");
            if (angle == 90 && (Math.Abs(bounds.Width - 60) > 1e-8 || Math.Abs(bounds.Height - 100) > 1e-8)) throw new Exception("Image bounds did not rotate");
        }
        var group = new SceneObject { Kind = ObjectKind.Group, X = 123, Y = -45, Angle = 37 }; scene.Canvas.Children.Add(group);
        var before = scene.World(image); scene.Reparent(image, group);
        foreach (var point in new[] { new Point(), center, new Point(100, 60) })
            if ((before.Transform(point) - scene.World(image).Transform(point)).Length > 1e-8) throw new Exception("Reparenting moved rotated image");
        scene.Reparent(image, scene.Canvas);
        if ((before.Transform(center) - scene.World(image).Transform(center)).Length > 1e-8) throw new Exception("Ungrouping moved image");
        var restored = SceneDocument.Restore(scene.Snapshot()); restored.Validate();
        if ((restored.World(restored.Find(image.Id)!).Transform(center) - before.Transform(center)).Length > 1e-8) throw new Exception("Rotation persistence failed");
        Console.WriteLine("PASS: Image center rotation, rendered bounds, rotated-parent reparenting and persistence");
    }
}
