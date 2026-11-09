using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using GridSketch;

internal static class DuplicateTargetChecks
{
    public static void Run(MainWindow window)
    {
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        var scene = new SceneDocument();
        var parent = new SceneObject { Kind = ObjectKind.Group, X = 30, Angle = 25 };
        var a = new SceneObject { Kind = ObjectKind.Shape, Name = "A", X = 10 };
        var b = new SceneObject { Kind = ObjectKind.Text, Name = "B", Y = 20 };
        var target = new SceneObject { Kind = ObjectKind.Group, X = 80, Angle = -15, Children = [new() { Kind = ObjectKind.Shape }] };
        parent.Children = [a, b]; scene.Canvas.Children = [parent, target];
        string before = scene.Snapshot();
        var targets = scene.DuplicateTargets(new[] { b, a });
        Check(targets.Contains(parent) && targets.Contains(target) && scene.Snapshot() == before, "Duplicate target filtering must include current parent without mutating sources");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        Set("document", scene); Set("selectedId", a.Id); Call("RefreshAll");
        var selection = (HashSet<string>)typeof(MainWindow).GetField("layerSelection", flags)!.GetValue(window)!;
        selection.Clear(); selection.UnionWith([a.Id, b.Id]);
        IEnumerable<TreeViewItem> Items(ItemsControl root)
        { foreach (var item in root.Items.OfType<TreeViewItem>()) { yield return item; foreach (var child in Items(item)) yield return child; } }
        void Answer(bool accept)
        {
            var dialog = Application.Current.Windows.OfType<ReparentDialog>().Single();
            if (!accept) { dialog.Close(); return; }
            var tree = PropertyChecks.Find<TreeView>(dialog, "複製先のレイヤー");
            Items(tree).Single(i => Equals(i.Tag, target.Id)).IsSelected = true;
            PropertyChecks.Find<Button>(dialog, "選択した親へ複製").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        window.Dispatcher.BeginInvoke(new Action(() => Answer(false))); Call("ChooseDuplicateParent");
        Check(scene.Snapshot() == before, "Cancel must leave original scene unchanged");
        window.Dispatcher.BeginInvoke(new Action(() => Answer(true))); Call("ChooseDuplicateParent");
        Check(parent.Children.SequenceEqual(new[] { a, b }) && target.Children.Count == 3, $"Duplicate must retain originals and append copies: parent={parent.Children.Count}, target={target.Children.Count}, selected={selection.Count}");
        var ca = target.Children[1]; var cb = target.Children[2];
        Check(ca.Name == "A" && cb.Name == "B" && ca.Id != a.Id && cb.Id != b.Id, "Copy identities, names and order failed");
        Check(ca.X == a.X && ca.Y == a.Y && ca.Angle == a.Angle && cb.X == b.X && cb.Y == b.Y,
            "Duplicate must preserve parent-relative coordinates");
        Check(selection.SetEquals([ca.Id, cb.Id]), "New copies should remain selected together");
        Call("Undo"); var undone = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        Check(undone.Snapshot() == before, "Batch duplicate should undo in one step");
        var repeated = scene.DuplicateInto(new[] { a }, target).Single();
        Check(repeated.Name == "A のコピー", "Name collision in destination must add copy suffix");
        var other = new SceneObject { Kind = ObjectKind.Group }; scene.Canvas.Children.Add(other);
        Check(scene.DuplicateInto(new[] { repeated }, other).Single().Name == "A", "Copy suffix must be removed in another tree without collision");
        var localCopy = scene.DuplicateInto(new[] { a }, parent).Single();
        Check(localCopy.Name == "A のコピー" && localCopy.X == a.X, "Same-parent copy must keep relative position and disambiguate name");
        a.UseParentLayout = true; a.LayoutAnchor = "右下"; a.LayoutSizing = "収める";
        var layoutCopy = scene.DuplicateInto(new[] { a }, other).Single();
        Check(layoutCopy.UseParentLayout && layoutCopy.LayoutAnchor == a.LayoutAnchor && layoutCopy.LayoutSizing == a.LayoutSizing,
            "Copies must preserve parent layout settings");
        var grid = SceneDocument.CreateGrid("空のグリッド"); scene = new SceneDocument(); scene.Canvas.Children.Add(grid);
        var selector = new SceneObject { Kind = ObjectKind.CellSelection }; grid.Children.Add(selector);
        var template = new SceneObject { Kind = ObjectKind.Group, Children = [new() { Kind = ObjectKind.Shape }] };
        selector.Children.Add(template); SceneDocument.AssignNewState(selector, template);
        SceneDocument.SetCellState(selector, new(0, 0), selector.States[0].Id);
        var copiedSelector = scene.DuplicateInto(new[] { selector }, grid).Single();
        Check(copiedSelector.States[0].Id != selector.States[0].Id && copiedSelector.States[0].ObjectId == copiedSelector.Children[0].Id
            && copiedSelector.StateCells[0].StateId == copiedSelector.States[0].Id, "Cloned selector must remap all internal references");
        scene.DuplicateInto(new[] { template }, selector);
        Check(selector.States.Count == 2 && selector.States[1].ObjectId == selector.Children[1].Id, "Copying a template into same selector adds an assignment");
        Console.WriteLine("PASS: Duplicate target dialog, cancel, batch order, relative placement, naming, layout, undo and state remapping");
    }
}
