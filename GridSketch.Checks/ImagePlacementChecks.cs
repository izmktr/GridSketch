using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GridSketch;

internal static class ImagePlacementChecks
{
    public static void Run(MainWindow window)
    {
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); }
        var scene = new SceneDocument(); var grid = SceneDocument.CreateGrid("空のグリッド", 4, 4, 48); scene.Canvas.Children.Add(grid);
        var group = new SceneObject { Kind = ObjectKind.Group, OrganizeOnly = true }; grid.Children.Add(group);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(BitmapSource.Create(24, 12, 96, 96, PixelFormats.Bgra32, null, new byte[24 * 12 * 4], 96)));
        using var stream = new MemoryStream(); png.Save(stream);
        var node = new SceneObject { Kind = ObjectKind.Image, ImageData = stream.ToArray(), SnapToCell = true, Column = 1, Row = 2, X = 24 };
        group.Children.Add(node); group.Children.Add(new SceneObject { Kind = ObjectKind.Text }); scene.Validate();
        var bounds = new SceneRenderer(scene).Bounds[node.Id];
        scene.SetImageCellPlacement(node, false); scene.Validate();
        Check(new SceneRenderer(scene).Bounds[node.Id] == bounds, "Free placement preserves a cell image's visible size and position");
        Check(!SceneDocument.RequiresGrid(group), "Free image and text no longer block group transforms");
        scene.EnableGroupTransform(group); Check(!group.OrganizeOnly, "Group can return to transform mode");
        scene.SetImageCellPlacement(node, true); scene.Validate();
        Check(node.SnapToCell && node.Column == 2 && node.Row == 2 && group.OrganizeOnly, "Cell placement picks nearest cell and prepares parent groups");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MainWindow).GetField("document", flags)!.SetValue(window, scene);
        typeof(MainWindow).GetField("selectedId", flags)!.SetValue(window, node.Id);
        typeof(MainWindow).GetMethod("RefreshAll", flags)!.Invoke(window, null);
        var panel = (StackPanel)window.FindName("Properties");
        var free = PropertyChecks.Find<ToggleButton>(panel, "配置方式：自由配置");
        var cell = PropertyChecks.Find<ToggleButton>(panel, "配置方式：マス配置");
        Check(free.IsEnabled && cell.IsChecked == true, "Placement toggle is available inside a group");
        free.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Check(!node.SnapToCell, "Placement toggle applies immediately");
        typeof(MainWindow).GetMethod("Undo", flags)!.Invoke(window, null);
        var restored = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        Check(restored.Find(node.Id)!.SnapToCell, "Placement conversion supports undo");
        Console.WriteLine("PASS: Image placement conversion, visual preservation, nearest cell, group transforms, toggle and undo");
    }
}
