using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GridSketch;

internal static class DiagonalBorderChecks
{
    public static void Run(MainWindow window, string directory)
    {
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        var scene = new SceneDocument();
        var grid = SceneDocument.CreateGrid("空のグリッド", 4, 4, 40); grid.SeparateCellHeight = 60;
        var border = new SceneObject { Kind = ObjectKind.Border, ConnectCellCenters = true, CenterDirections = "斜め", Color = "#80000000", Thickness = 3 };
        scene.Canvas.Children.Add(grid); grid.Children.Add(border);
        var down = new GridEdge(0, 0, false) { Diagonal = 1 };
        var up = down with { Diagonal = -1 };
        Check(SceneDocument.CenterEdgeAt(border, grid, new Point(30, 45), 1) == down, "Diagonal hit test on rectangular cells failed");
        Check(SceneDocument.CenterEdgeAt(border, grid, new Point(30, 75), 2) == up, "Ascending diagonal hit test failed");
        Check(SceneDocument.CenterEdgeAt(border, grid, new Point(40, 30), 1) is null, "Diagonal mode must ignore horizontal edges");
        var stroke = new GridPaintStroke(scene, border, grid, new Point(30, 45), down);
        stroke.Move(new Point(115, 172.5));
        Check(border.Edges.SetEquals([down, down with { Column = 1, Row = 1 }, down with { Column = 2, Row = 2 }]), "Diagonal drag must bridge missing events");
        stroke = new GridPaintStroke(scene, border, grid, new Point(30, 45), down); stroke.Move(new Point(115, 172.5));
        Check(border.Edges.Count == 0, "Diagonal erase drag failed");
        border.Edges = [down, up];
        scene.Validate();
        var path = Path.Combine(directory, "diagonal.gridsketch"); scene.Save(path);
        Check(SceneDocument.Load(path).Snapshot() == scene.Snapshot(), "Diagonal round trip failed");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(new SceneRenderer(scene).Export()));
        using (var output = File.Create(Path.Combine(directory, "diagonal.png"))) encoder.Save(output);
        PdfExporter.Save(scene, Path.Combine(directory, "diagonal.pdf"));
        scene.EditGridStructure(grid, true, 1, 1, false);
        Check(border.Edges.Count == 0, "Inserted rows must break diagonal links across the insertion");
        border.Edges = [down with { Column = 1, Row = 2 }];
        scene.EditGridStructure(grid, false, 0, 1, true);
        Check(border.Edges.SetEquals([down with { Row = 2 }]), "Column removal must shift diagonal links");
        scene.ResizeGrid(grid, 1, 1, 40, 5);
        Check(border.Edges.Count == 0, "Grid shrinking must crop diagonal links");
        scene.ResizeGrid(grid, 3, 3, 40, 5);
        border.Pattern = "すべて";
        Check(SceneDocument.BorderEdges(border, grid).Count() == 8, "Diagonal preset failed");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        void Call(string name) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, null);
        Set("document", scene); Set("selectedId", border.Id); Call("RefreshAll");
        var panel = (StackPanel)window.FindName("Properties");
        var both = PropertyChecks.Find<ToggleButton>(panel, "接続方向：縦横＋斜め");
        both.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        both.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(both.IsChecked == true && border.CenterDirections == "縦横＋斜め" && border.Edges.Count == 8,
            "Direction icons must stay exclusive and preserve existing lines");
        window.UpdateLayout();
        var row = (WrapPanel)both.Parent;
        ((Panel)row.Parent).Children.Remove(row);
        row.Background = Brushes.White;
        var preview = new Window { Content = row, Width = 280, Height = 120, ShowInTaskbar = false };
        preview.Show(); preview.UpdateLayout();
        var png = new RenderTargetBitmap(248, 64, 96, 96, PixelFormats.Pbgra32); png.Render(row);
        encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(png));
        using (var output = File.Create(Path.Combine(directory, "direction-icons.png"))) encoder.Save(output);
        preview.Close();
        LivePropertyChecks.Commit(window); Call("Undo");
        var restored = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        Check(restored.Find(border.Id)!.CenterDirections == "斜め", "Direction selection undo failed");
        Set("savedState", restored.Snapshot());
        Console.WriteLine("PASS: Diagonal hit testing, drag/erase, persistence, export, grid edits, icon selection and undo");
    }
}
