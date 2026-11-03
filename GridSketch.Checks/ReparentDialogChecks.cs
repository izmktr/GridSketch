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
        Set("savedState", restored.Snapshot());
        Console.WriteLine("PASS: Legal parent targets, non-mutating validation, filtered hierarchy, modal selection, cancellation, world placement and undo");
    }

    private sealed class DisposableDialog(ReparentDialog window) : IDisposable
    {
        public ReparentDialog Window { get; } = window;
        public void Dispose() => Window.Close();
    }
}
