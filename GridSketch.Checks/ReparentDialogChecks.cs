using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GridSketch;

internal static class ReparentDialogChecks
{
    public static void Run(MainWindow window, string directory)
    {
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        var scene = new SceneDocument();
        var source = new SceneObject { Kind = ObjectKind.Group, Name = "移動するグループ", X = 30, Y = 50, Children = [new() { Kind = ObjectKind.Group }] };
        var target = new SceneObject { Kind = ObjectKind.Group, Name = "移動先", X = 80, Y = 20, Angle = 20 };
        var locked = new SceneObject { Kind = ObjectKind.Group, Locked = true, Children = [new() { Kind = ObjectKind.Group }] };
        var grid = SceneDocument.CreateGrid("空のグリッド", 4, 4, 40);
        var secondGrid = SceneDocument.CreateGrid("空のグリッド", 4, 4, 40);
        var text = new SceneObject { Kind = ObjectKind.GridText };
        var group = new SceneObject { Kind = ObjectKind.Group, Name = "整理用へ変換可能" };
        var transformed = new SceneObject { Kind = ObjectKind.Group, X = 5 };
        var selector = new SceneObject { Kind = ObjectKind.CellSelection };
        for (int i = 0; i < 128; i++) selector.States.Add(new() { Name = "状態" });
        grid.Children = [text, group, transformed, selector];
        scene.Canvas.Children = [source, target, locked, grid, secondGrid]; scene.Validate();
        string before = scene.Snapshot();
        var candidates = scene.ReparentTargets(source);
        Check(candidates.Contains(target) && !candidates.Contains(source) && !candidates.Contains(source.Children[0]) && !candidates.Contains(scene.Canvas)
            && !candidates.Contains(locked) && !candidates.Contains(locked.Children[0]) && !candidates.Contains(selector), "Parent target filtering failed for cycles, current parent, locks or state capacity");
        var gridTargets = scene.ReparentTargets(text);
        Check(gridTargets.Contains(group) && !gridTargets.Contains(transformed) && !gridTargets.Contains(secondGrid) && !gridTargets.Contains(scene.Canvas), "Grid-dependent targets do not respect conversion and grid boundaries");
        Check(!scene.ReparentTargets(grid).Contains(selector), "Grid offered as a cell selection template");
        Check(scene.ReparentTargets(scene.Canvas).Count == 0 && scene.ReparentTargets(locked.Children[0]).Count == 0, "Root or locked child allowed to move");
        Check(scene.Snapshot() == before && !group.OrganizeOnly, "Building candidates changed the real document");
        using (var unused = new DisposableDialog(new ReparentDialog(scene, text)))
        {
            var tree = PropertyChecks.Find<TreeView>(unused.Window, "移動先のレイヤー");
            Check(tree.Items.Count > 0 && tree.Items.OfType<TreeViewItem>().All(i => i.Tag is string id && gridTargets.Any(n => n.Id == id)), "Filtered tree contains non-movable ancestor rows");
        }

        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        void Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        Set("document", scene); Set("selectedId", source.Id); Call("RefreshAll");
        var world = scene.World(source);
        IEnumerable<TreeViewItem> Items(ItemsControl parent)
        {
            foreach (var item in parent.Items.OfType<TreeViewItem>()) { yield return item; foreach (var child in Items(item)) yield return child; }
        }
        void DialogAction(bool accept)
        {
            var dialog = Application.Current.Windows.OfType<ReparentDialog>().Single();
            Check(ComponentDispatcher.IsThreadModal && dialog.Owner == window, "Parent chooser is not modal with the correct owner");
            dialog.UpdateLayout();
            var tree = PropertyChecks.Find<TreeView>(dialog, "移動先のレイヤー");
            var move = PropertyChecks.Find<Button>(dialog, "選択した親へ移動");
            Check(!move.IsEnabled, "Move enabled before choosing a target");
            Items(tree).Single(i => Equals(i.Tag, target.Id)).IsSelected = true;
            Check(move.IsEnabled && dialog.SelectedParentId == target.Id, "Tree selection did not enable move");
            if (!accept) { dialog.Close(); return; }
            var image = new RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(dialog);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (var output = File.Create(Path.Combine(directory, "reparent-dialog.png"))) encoder.Save(output);
            move.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        window.Dispatcher.BeginInvoke(new Action(() => DialogAction(false)));
        Call("ChooseParent", source);
        Check(scene.Snapshot() == before, "Cancel changed the document");
        window.Dispatcher.BeginInvoke(new Action(() => DialogAction(true)));
        Call("ChooseParent", source);
        Check(scene.Parent(source) == target && (scene.World(source).Transform(new Point(4, 9)) - world.Transform(new Point(4, 9))).Length < 1e-7, "Confirmed move failed to preserve placement");
        Call("Undo");
        var restored = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        Check(restored.Snapshot() == before, "Parent move is not a single undo action");
        var batch = new SceneDocument();
        var origin = new SceneObject { Kind = ObjectKind.Group, X = 20, Angle = 30 };
        var destination = new SceneObject { Kind = ObjectKind.Group, X = 90, Angle = -20 };
        var a = new SceneObject { Kind = ObjectKind.Shape, X = 12 };
        var b = new SceneObject { Kind = ObjectKind.Text, Y = 25 };
        var existing = new SceneObject { Kind = ObjectKind.Shape };
        origin.Children = [a, b]; destination.Children.Add(existing); batch.Canvas.Children = [origin, destination];
        var aw = batch.World(a); var bw = batch.World(b);
        Check(batch.ReparentTargets(new[] { b, a }).Contains(destination), "Batch target not offered");
        Check(batch.ReparentTargets(new[] { a, existing }).Count == 0 && batch.ReparentTargets(new[] { origin, a }).Count == 0,
            "Different parents and ancestor/descendant selection must be rejected");
        b.Locked = true; Check(batch.ReparentTargets(new[] { a, b }).Count == 0, "Locked member must block batch"); b.Locked = false;
        var batchBefore = batch.Snapshot();
        Set("document", batch); Set("selectedId", a.Id); Call("RefreshAll");
        window.Dispatcher.BeginInvoke(new Action(() =>
        {
            var dialog = Application.Current.Windows.OfType<ReparentDialog>().Single();
            var tree = PropertyChecks.Find<TreeView>(dialog, "移動先のレイヤー");
            Items(tree).Single(i => Equals(i.Tag, destination.Id)).IsSelected = true;
            PropertyChecks.Find<Button>(dialog, "選択した親へ移動").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
        Call("ChooseParents", new List<SceneObject> { b, a });
        Check(destination.Children.SequenceEqual(new[] { existing, a, b }), "Batch must append in original layer order");
        Check((batch.World(a).Transform(new Point(3, 4)) - aw.Transform(new Point(3, 4))).Length < 1e-7 &&
            (batch.World(b).Transform(new Point(3, 4)) - bw.Transform(new Point(3, 4))).Length < 1e-7, "Batch must preserve world transforms");
        Call("Undo"); restored = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        Check(restored.Snapshot() == batchBefore, "Batch parent change must undo as one action");
        var templatesScene = new SceneDocument();
        var templatesGrid = SceneDocument.CreateGrid("空のグリッド"); templatesScene.Canvas.Children.Add(templatesGrid);
        var owner = new SceneObject { Kind = ObjectKind.CellSelection, UseObjectLayout = true }; templatesGrid.Children.Add(owner);
        var firstTemplate = new SceneObject { Kind = ObjectKind.Group, HasLayoutFrame = true, Width = 100, Height = 100,
            UseParentLayout = true, LayoutRegion = "親の内側", LayoutSizing = "収める", Children = [new() { Kind = ObjectKind.Shape }] };
        var secondTemplate = new SceneObject { Kind = ObjectKind.Group, HasLayoutFrame = true, Width = 100, Height = 100,
            UseParentLayout = true, LayoutRegion = "親の内側", LayoutSizing = "収める", Children = [new() { Kind = ObjectKind.Text }] };
        owner.Children = [firstTemplate, secondTemplate];
        SceneDocument.AssignNewState(owner, firstTemplate); SceneDocument.AssignNewState(owner, secondTemplate);
        SceneDocument.SetCellState(owner, new(0, 0), owner.States[0].Id); templatesScene.Validate();
        var templatesBefore = templatesScene.Snapshot();
        Check(templatesScene.ReparentTargets(new[] { secondTemplate, firstTemplate }).Contains(templatesGrid),
            "Multiple templates must be able to move to their enclosing grid");
        Check(templatesScene.Snapshot() == templatesBefore, "Template candidate calculation must not change assignments");
        Set("document", templatesScene); Set("selectedId", firstTemplate.Id); Call("RefreshAll");
        window.Dispatcher.BeginInvoke(new Action(() =>
        {
            var dialog = Application.Current.Windows.OfType<ReparentDialog>().Single();
            Items(PropertyChecks.Find<TreeView>(dialog, "移動先のレイヤー")).Single(i => Equals(i.Tag, templatesGrid.Id)).IsSelected = true;
            PropertyChecks.Find<Button>(dialog, "選択した親へ移動").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
        Call("ChooseParents", new List<SceneObject> { secondTemplate, firstTemplate });
        Check(templatesGrid.Children.SequenceEqual(new[] { owner, firstTemplate, secondTemplate }) && owner.Children.Count == 0,
            "Multiple templates append to the grid in layer order");
        Check(owner.States.All(s => s.ObjectId is null) && owner.StateCells.Count == 1, "Moving templates clears assignments without deleting cell states");
        Call("Undo"); restored = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        Check(restored.Snapshot() == templatesBefore, "Undo restores all template groups and their assignments together");
        Set("savedState", restored.Snapshot());
        Console.WriteLine("PASS: Legal parent targets, non-mutating validation, filtered hierarchy, modal selection, cancellation, world placement and undo");
    }

    private sealed class DisposableDialog(ReparentDialog window) : IDisposable
    {
        public ReparentDialog Window { get; } = window;
        public void Dispose() => Window.Close();
    }
}
