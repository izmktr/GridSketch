using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using GridSketch;

internal static class PropertyToggleChecks
{
    public static void Run(MainWindow window)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        void Call(string name) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, null);
        var scene = new SceneDocument(); var group = new SceneObject { Kind = ObjectKind.Group };
        var shape = new SceneObject { Kind = ObjectKind.Shape }; group.Children.Add(shape); scene.Canvas.Children.Add(group);
        Set("document", scene); Set("selectedId", shape.Id); Call("RefreshAll");
        var panel = (StackPanel)window.FindName("Properties");
        var visible = (ToggleButton)window.FindName("VisibilityToggle"); var locked = (ToggleButton)window.FindName("LockToggle");
        var width = PropertyChecks.Find<TextBox>(panel, "幅（px）"); width.Text = "234.5";
        visible.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if (shape.Visible || width.Text != "234.5" || !ReferenceEquals(width, PropertyChecks.Find<TextBox>(panel, "幅（px）"))) throw new Exception("Visibility toggle lost pending edits or was not immediate");
        locked.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if (!shape.Locked || locked.IsChecked != true || width.Text != "234.5") throw new Exception("Immediate lock failed");
        locked.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        LivePropertyChecks.Commit(window);
        if (shape.Width != 234.5 || shape.Visible || shape.Locked) throw new Exception("Apply reset toggle flags or lost pending size");
        Call("Undo");
        scene = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        if (!scene.Find(shape.Id)!.Locked || scene.Find(shape.Id)!.Visible) throw new Exception("Toggle undo failed");
        Call("Redo");
        scene = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        if (scene.Find(shape.Id)!.Locked) throw new Exception("Toggle redo failed");
        scene.Find(group.Id)!.Locked = true; Call("RefreshAll");
        if (visible.IsEnabled || locked.IsEnabled) throw new Exception("Locked parent allowed child flags to change");
        Set("selectedId", scene.Canvas.Id); Call("RefreshAll");
        if (visible.Visibility != Visibility.Collapsed || locked.Visibility != Visibility.Collapsed) throw new Exception("Canvas unexpectedly exposes visibility or lock");
        Set("savedState", scene.Snapshot());
        Console.WriteLine("PASS: Immediate property icons, pending editor preservation, apply, undo/redo, parent lock and canvas behavior");
    }
}
