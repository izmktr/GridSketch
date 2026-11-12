using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GridSketch;

internal static class ShortcutChecks
{
    public static void Run(MainWindow window, string directory)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Field(string name) => typeof(MainWindow).GetField(name, flags)!.GetValue(window);
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        bool Press(Key key, ModifierKeys mods = ModifierKeys.None, bool typing = false) => (bool)Call("HandleEditorShortcut", key, mods, typing)!;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        var scene = new SceneDocument(); var shape = new SceneObject { Kind = ObjectKind.Shape }; scene.Canvas.Children.Add(shape);
        var file = Path.Combine(directory, "shortcut-first.gridsketch"); scene.Save(file);
        Call("AddProjectTab", scene, file); var first = Field("activeTab")!;
        Set("selectedId", shape.Id); Call("RefreshAll");
        Check(!Press(Key.D, ModifierKeys.Control, true) && scene.Canvas.Children.Count == 1, "Typing must not duplicate objects");
        Press(Key.D, ModifierKeys.Control); Check(scene.Canvas.Children.Count == 2, "Ctrl+D must duplicate selection");
        var layers = (TreeView)window.FindName("Layers");
        IEnumerable<TreeViewItem> Items(ItemsControl parent)
        {
            foreach (var item in parent.Items.OfType<TreeViewItem>())
            { yield return item; foreach (var child in Items(item)) yield return child; }
        }
        var selectedItem = Items(layers).Single(i => Equals(i.Tag, Field("selectedId")));
        foreach (var item in Items(layers)) item.IsExpanded = true;
        window.Activate(); window.UpdateLayout(); selectedItem.BringIntoView();
        selectedItem.Focus(); Press(Key.F2);
        Check(selectedItem.Header is TextBox, "F2 in the layer list must begin inline renaming");
        ((TextBox)selectedItem.Header).Text = "名前変更の検証";
        ((Action)Field("commitLayerNameEdit")!).Invoke();
        Check(scene.Find((string)Field("selectedId")!)!.Name == "名前変更の検証", "F2 rename must commit normally");
        Press(Key.V); Check(!(bool)Field("cellEditMode")!, "V must select move mode");
        Check(!Press(Key.E, typing: true) && !(bool)Field("cellEditMode")!, "Typing E must not change modes");
        Press(Key.E); Check((bool)Field("cellEditMode")!, "E must select edit mode");
        var zoom = (Slider)window.FindName("Zoom"); zoom.Value = 2; Press(Key.D0, ModifierKeys.Control);
        Check(zoom.Value == 1, "Ctrl+0 must restore 100 percent zoom"); Press(Key.Home);
        var other = new SceneDocument(); var otherFile = Path.Combine(directory, "shortcut-second.gridsketch"); other.Save(otherFile);
        Call("AddProjectTab", other, otherFile); var second = Field("activeTab")!; other.Canvas.Background = "#123456";
        Press(Key.Tab, ModifierKeys.Control | ModifierKeys.Shift); Check(ReferenceEquals(Field("activeTab"), first), "Ctrl+Shift+Tab must select previous tab");
        Press(Key.Tab, ModifierKeys.Control); Check(ReferenceEquals(Field("activeTab"), second), "Ctrl+Tab must select next tab");
        // Only exercise saving these two tabs; other test fixtures may intentionally be unsaved.
        var tabs = (IList)Field("editorTabs")!;
        var previous = tabs.Cast<object>().Where(t => t != first && t != second).ToList();
        foreach (var tab in previous) tabs.Remove(tab);
        try
        {
            Press(Key.S, ModifierKeys.Control | ModifierKeys.Shift);
            Check(SceneDocument.Load(file).Canvas.Children.Count == 2 && SceneDocument.Load(otherFile).Canvas.Background == "#123456"
                && ReferenceEquals(Field("activeTab"), second), "Save all must save each tab and restore the active tab");
            Press(Key.F4, ModifierKeys.Control); Check(tabs.Count == 1 && ReferenceEquals(Field("activeTab"), first), "Ctrl+F4 must close the current tab");
            Press(Key.W, ModifierKeys.Control); Check(tabs.Count == 0, "Ctrl+W must also close the current tab");
        }
        finally
        {
            foreach (var tab in previous) tabs.Add(tab);
            if (previous.Count > 0) Call("SwitchTab", previous[^1]);
        }
        Console.WriteLine("PASS: Duplicate/save-all, Ctrl+W/Ctrl+F4, tab navigation, mode keys, zoom and text-input guards");
    }
}
