using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using GridSketch;

internal static class GroupLayoutChecks
{
    public static void Run(MainWindow window, string directory)
    {
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); }
        var doc = new SceneDocument(); var grid = SceneDocument.CreateGrid("基本の方眼紙"); doc.Canvas.Children.Add(grid);
        var border = grid.Children.First(n => n.Kind == ObjectKind.Border);
        var outer = new SceneObject { Kind = ObjectKind.Group }; var inner = new SceneObject { Kind = ObjectKind.Group };
        grid.Children.Add(outer); outer.Children.Add(inner);
        var bounds = new SceneRenderer(doc).OutputBounds;
        doc.Reparent(border, inner); doc.Validate();
        Check(inner.OrganizeOnly && outer.OrganizeOnly && doc.GridFor(border) == grid, "Nested groups auto-convert and inherit grid");
        Check(new SceneRenderer(doc).OutputBounds == bounds, "Grouping preserves drawing extent");
        inner.Visible = false; Check(!doc.Shown(border) && border.Visible, "Group hides children without changing visibility flags"); inner.Visible = true;
        try { doc.EnableGroupTransform(outer); throw new Exception("Dependent group enabled transforms"); } catch (InvalidOperationException) { }
        doc.Reparent(border, grid); doc.EnableGroupTransform(inner); doc.EnableGroupTransform(outer);
        Check(!inner.OrganizeOnly && !outer.OrganizeOnly, "Transform mode can be explicitly restored");
        outer.Angle = 15; var snapshot = doc.Snapshot();
        try { doc.Reparent(border, inner); throw new Exception("Transformed ancestor accepted content"); } catch (InvalidOperationException) { }
        Check(doc.Snapshot() == snapshot, "Failed conversion leaves hierarchy and group modes unchanged"); outer.Angle = 0;
        doc.Reparent(border, inner);
        var other = SceneDocument.CreateGrid("空のグリッド"); doc.Canvas.Children.Add(other);
        try { doc.Reparent(outer, other); throw new Exception("Dependent group moved to another grid"); } catch (InvalidOperationException) { }
        var text = new SceneObject { Kind = ObjectKind.GridText }; grid.Children.Add(text); doc.Reparent(text, inner);
        text.Glyphs.Add(new Glyph { Column = 15, Row = 0, Text = "A" });
        try { doc.ResizeGrid(grid, 5, 5, 48, 5); throw new Exception("Nested text was discarded by resize"); } catch (InvalidOperationException) { }
        text.Glyphs.Clear(); doc.ResizeGrid(grid, 5, 5, 48, 5); doc.Validate();
        var selector = new SceneObject { Kind = ObjectKind.CellSelection }; inner.Children.Add(selector); doc.PrepareGroupLayouts(); doc.Validate();
        var wrapper = new SceneObject { Kind = ObjectKind.Group, X = 12, Angle = 20 }; doc.Canvas.Children.Add(wrapper); doc.Reparent(grid, wrapper); doc.Validate();
        Check(!wrapper.OrganizeOnly, "Wrapping a whole grid remains transformable");
        var file = Path.Combine(directory, "group-layout.gridsketch"); doc.Save(file);
        Check(SceneDocument.Load(file).Snapshot() == doc.Snapshot(), "Group modes survive save and load");
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        void Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        Set("document", doc); Set("selectedId", border.Id); Call("RefreshAll");
        Call("WrapClick", window, new RoutedEventArgs());
        var added = doc.Parent(border)!;
        Check(added.Kind == ObjectKind.Group && added.OrganizeOnly, "UI grouping of a border auto-converts");
        var panel = (StackPanel)window.FindName("Properties");
        Button RestoreButton() => PropertyChecks.Section(panel, "グループ").Children.OfType<Button>().Single(b => Equals(b.Content, "一括変形用に戻す"));
        Check(!RestoreButton().IsEnabled, "Restore button disabled while dependent content exists");
        Set("selectedId", border.Id); Call("RefreshAll"); Call("Change", new Action(() => doc.ReparentMany(new[] { border }, inner)));
        Check(doc.Parent(border) == inner, "Dependent content can leave organizer within its grid");
        Set("selectedId", added.Id); Call("RefreshAll"); panel = (StackPanel)window.FindName("Properties");
        Check(added.OrganizeOnly && RestoreButton().IsEnabled, "Empty organizer remains organizer until explicit restoration");
        RestoreButton().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(!added.OrganizeOnly, "Restore button enables group transformation");
        Call("Undo"); doc = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        Check(doc.Find(added.Id)!.OrganizeOnly, "Mode restoration is undoable");
        Set("savedState", doc.Snapshot());
        Console.WriteLine("PASS: Organizing groups, nested conversion, transform rejection, visibility, resize and persistence");
    }
}
