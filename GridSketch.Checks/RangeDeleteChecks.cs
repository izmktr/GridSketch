using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GridSketch;

internal static class RangeDeleteChecks
{
    public static void Run(MainWindow window)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        void Call(string name) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, null);
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        var doc = new SceneDocument(); var grid = SceneDocument.CreateGrid("空のグリッド", 4, 4); doc.Canvas.Children.Add(grid);
        var text = new SceneObject { Kind = ObjectKind.GridText, Glyphs = [new() { Column = 0, Row = 0, Text = "A" }, new() { Column = 1, Row = 1, Text = "B" }, new() { Column = 2, Row = 2, Text = "C" }] };
        var border = new SceneObject { Kind = ObjectKind.Border, ConnectCellCenters = true, Edges = [new(0, 0, false), new(1, 0, false), new GridEdge(0, 0, false) { Diagonal = 1 }, new GridEdge(0, 0, false) { Diagonal = -1 }] };
        grid.Children = [text, border]; Set("document", doc); Set("selectedId", text.Id); Set("cellEditMode", true); Call("RefreshAll");
        var surface = (SceneSurface)window.FindName("Surface"); var viewport = (ScrollViewer)window.FindName("Viewport");
        void Select() { surface.ActiveCell = new(1, 1); surface.RangeEnd = new(0, 0); viewport.Focus(); }
        void Delete() => viewport.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Delete) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Select(); Delete();
        Check(text.Glyphs.Count == 1 && text.Glyphs[0].Text == "C" && doc.Find(text.Id) == text, "Range Delete must remove only selected glyphs, not the layer");
        Check(surface.RangeEnd == new CellPosition(0, 0), "Range Delete must retain the selected range");
        Call("Undo"); doc = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        Check(doc.Find(text.Id)!.Glyphs.Count == 3, "Range deletion must undo in one step");
        Set("selectedId", border.Id); Call("RefreshAll"); Select(); Delete();
        border = doc.Find(border.Id)!;
        Check(border.Edges.SetEquals([new GridEdge(1, 0, false)]), "Center range deletion must include both diagonals and retain crossing-boundary links");
        border.Locked = true; Call("RefreshAll"); Select(); Delete();
        Check(border.Edges.Count == 1, "Range Delete must respect locks");
        Set("savedState", doc.Snapshot());
        Console.WriteLine("PASS: Delete selected grid text/center lines, diagonal endpoints, selection retention, undo and locks");
    }
}
