using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GridSketch;

internal static class LineCoordinateChecks
{
    public static void Run(MainWindow window, string directory)
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        bool Near(Point a, Point b) => (a - b).Length < 1e-7;
        var doc = new SceneDocument(); var grid = SceneDocument.CreateGrid("空のグリッド", 10, 10, 48);
        grid.SeparateCellHeight = 72; doc.Canvas.Children.Add(grid);
        var group = new SceneObject { Kind = ObjectKind.Group, OrganizeOnly = true }; grid.Children.Add(group);
        var line = new SceneObject { Kind = ObjectKind.Line, PointLine = true, PathKind = "曲線" };
        group.Children.Add(line); SceneDocument.InitializeGridCoordinates(line); doc.AdoptGridCoordinates(line);
        doc.SetLinePoint(line, LineHandle.End, new(3, 2)); doc.SetLinePoint(line, LineHandle.Control, new(2, .5));
        Check(Near(doc.World(line).Transform(new Point()), new(24, 36)) && Near(doc.World(line).Transform(new Point(line.EndX, line.EndY)), new(120, 108)),
            "Line points use rectangular grid cell centers through organizing groups");
        var start = doc.LinePoint(line, LineHandle.Start); var control = doc.LinePoint(line, LineHandle.Control);
        doc.SetLinePoint(line, LineHandle.End, new(4, 3));
        Check(doc.LinePoint(line, LineHandle.Start) == start && doc.LinePoint(line, LineHandle.Control) == control, "Editing endpoint never moves start/control");
        var end = doc.LinePoint(line, LineHandle.End);
        doc.SetLinePoint(line, LineHandle.Start, new(1.5, 2));
        Check(doc.LinePoint(line, LineHandle.End) == end && doc.LinePoint(line, LineHandle.Control) == control, "Editing start never moves end/control");
        start = doc.LinePoint(line, LineHandle.Start);
        doc.MoveOnGrid(line, start, new(24, 36));
        Check(doc.LinePoint(line, LineHandle.Start) == start + new Vector(.5, .5)
            && doc.LinePoint(line, LineHandle.End) == end + new Vector(.5, .5), "Body drag translates every point by half-cell steps");
        start = doc.LinePoint(line, LineHandle.Start); end = doc.LinePoint(line, LineHandle.End); control = doc.LinePoint(line, LineHandle.Control);
        doc.ResizeGrid(grid, 10, 10, 64, 5, 5, 40);
        Check(doc.LinePoint(line, LineHandle.Start) == start && doc.LinePoint(line, LineHandle.End) == end
            && doc.LinePoint(line, LineHandle.Control) == control, "Rectangular cell resize preserves all displayed coordinates");
        new LineHandleDrag(doc, line, LineHandle.End).Move(new Vector(32, 20), 2);
        Check(doc.LinePoint(line, LineHandle.Start) == start && doc.LinePoint(line, LineHandle.End) == end + new Vector(.5, .5),
            "Endpoint drag snaps both axes independently without moving start");
        var other = SceneDocument.CreateGrid("空のグリッド", 10, 10, 96); doc.Canvas.Children.Add(other);
        end = doc.LinePoint(line, LineHandle.End);
        doc.ReparentMany([group], other);
        Check(doc.LinePoint(line, LineHandle.Start) == start && doc.LinePoint(line, LineHandle.End) == end, "Organizer reparent preserves point coordinates on a differently sized grid");
        var copy = doc.DuplicateInto([line], grid).Single();
        Check(doc.LinePoint(copy, LineHandle.Start) == start && doc.LinePoint(copy, LineHandle.End) == end, "Duplicate rescales pixel geometry to retain all cell coordinates");
        var part = LibraryPart.FromGroup(group, "線の部品"); var inserted = doc.InsertPart(part, grid).Children.Single().Children.Single();
        Check(inserted.EndX == line.EndX && inserted.EndY == line.EndY, "Part references preserve definition geometry within their own frame");
        var beforeStart = doc.World(copy).Transform(new Point()); var beforeEnd = doc.World(copy).Transform(new Point(copy.EndX, copy.EndY));
        var frame = doc.WrapContainers([copy], true);
        Check(!doc.UsesGridCoordinates(copy) && Near(doc.World(copy).Transform(new Point()), beforeStart)
            && Near(doc.World(copy).Transform(new Point(copy.EndX, copy.EndY)), beforeEnd), "Frame wrapping changes to px without moving points");
        frame.Angle = 33; beforeStart = doc.World(copy).Transform(new Point()); beforeEnd = doc.World(copy).Transform(new Point(copy.EndX, copy.EndY));
        doc.UnwrapContainer(frame);
        Check(copy.Angle == 0 && copy.PreservedTransform is null && Near(doc.World(copy).Transform(new Point()), beforeStart)
            && Near(doc.World(copy).Transform(new Point(copy.EndX, copy.EndY)), beforeEnd), "Rotated frame unwrap bakes rotation into editable points");
        doc.SetLinePoint(line, LineHandle.Start, new(1, 1)); doc.SetLinePoint(line, LineHandle.End, new(4, 1));
        doc.EditGridStructure(other, false, 2, 1, false);
        Check(doc.LinePoint(line, LineHandle.Start).X == 1 && doc.LinePoint(line, LineHandle.End).X == 5, "Column insertion maps each line point independently");
        doc.EditGridStructure(other, false, 2, 1, true);
        Check(doc.LinePoint(line, LineHandle.End).X == 4, "Column deletion restores endpoint coordinates");
        var path = Path.Combine(directory, "line-points.gridsketch"); doc.Save(path); var loaded = SceneDocument.Load(path);
        Check(loaded.Snapshot() == doc.Snapshot(), "Point coordinates survive save/load without repeated normalization");

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        void Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        var previousDocument = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        Set("savedState", previousDocument.Snapshot());
        Call("AddProjectTab", doc, path); Set("selectedId", line.Id); Call("RefreshAll"); window.UpdateLayout();
        var panel = (StackPanel)window.FindName("Properties");
        var section = PropertyChecks.Find<System.Windows.Controls.Primitives.ToggleButton>(panel, "経路の開閉");
        section.IsChecked = true; section.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        IEnumerable<DependencyObject> Walk(DependencyObject root)
        {
            yield return root;
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
                foreach (var n in Walk(child)) yield return n;
        }
        Check(!Walk(panel).Any(n => AutomationProperties.GetName(n).StartsWith("回転")
            || AutomationProperties.GetName(n).StartsWith("位置合わせ：") || AutomationProperties.GetName(n).StartsWith("サイズの扱い：")),
            "Line properties expose no rotation/alignment/fitting controls");
        var pathChoice = PropertyChecks.Find<ComboBox>(panel, "経路");
        var controlBox = PropertyChecks.Find<TextBox>(panel, "制御点 X（マス座標）");
        pathChoice.SelectedItem = "直線"; window.UpdateLayout();
        Check(!controlBox.IsVisible, "Straight lines hide unused control inputs immediately");
        pathChoice.SelectedItem = "曲線"; window.UpdateLayout();
        Check(controlBox.IsVisible, "Curves restore control inputs immediately");
        start = doc.LinePoint(line, LineHandle.Start);
        var endpointBox = PropertyChecks.Find<TextBox>(panel, "終点 X（マス座標）"); endpointBox.Text = "6.5";
        LivePropertyChecks.Commit(window);
        Check(doc.LinePoint(line, LineHandle.Start) == start && doc.LinePoint(line, LineHandle.End).X == 6.5, "Numeric endpoint edit holds start fixed");
        Call("Undo"); doc = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!; line = doc.Find(line.Id)!;
        Check(doc.LinePoint(line, LineHandle.End).X == 4, "Endpoint numeric edit is undoable");
        Set("selectedId", line.Id); Call("RefreshAll"); window.UpdateLayout();
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(window);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
        using (var stream = File.Create(Path.Combine(directory, "line-points-properties.png"))) png.Save(stream);
        Set("savedState", doc.Snapshot());
        Console.WriteLine("PASS: Line point units, stable endpoints, grid snapping/resizing, groups, frames, duplication, parts, row/column editing, persistence and UI");
    }
}
