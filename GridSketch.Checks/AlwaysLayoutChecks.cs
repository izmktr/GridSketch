using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GridSketch;

internal static class AlwaysLayoutChecks
{
    public static void Run(MainWindow window, string directory)
    {
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        bool Same(Matrix a, Matrix b) => (a.Transform(new Point(3, 7)) - b.Transform(new Point(3, 7))).Length < 1e-7
            && (a.Transform(new Point(19, -2)) - b.Transform(new Point(19, -2))).Length < 1e-7;
        var scene = new SceneDocument(); scene.Canvas.AutoSize = false; scene.Canvas.X = -20;
        var group = new SceneObject { Kind = ObjectKind.Group, X = 25, Y = 80, Angle = 15 };
        var text = new SceneObject { Kind = ObjectKind.Text, X = -12, Y = 18, Angle = 30, Locked = true };
        var shape = new SceneObject { Kind = ObjectKind.Shape, X = 41, Y = -9, Angle = -20 };
        scene.Canvas.Children.Add(group); group.Children = [text, shape];
        var matrices = scene.All().ToDictionary(n => n.Id, scene.World);
        var file = Path.Combine(directory, "legacy-layout-off.gridsketch"); File.WriteAllText(file, scene.Snapshot());
        var loaded = SceneDocument.Load(file);
        foreach (var node in loaded.All())
            Check(Same(loaded.World(node), matrices[node.Id]), "Layout migration must preserve nested world placement, including locked objects");
        Check(loaded.Find(text.Id)!.UseParentLayout && loaded.Find(text.Id)!.LayoutSizing == "固定", "Old OFF objects must migrate to active original size");
        string snapshot = loaded.Snapshot(); loaded.EnableParentLayouts();
        Check(loaded.Snapshot() == snapshot, "Layout migration must be idempotent");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        void Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        Set("document", loaded); Set("selectedId", shape.Id); Call("RefreshAll");
        var panel = (StackPanel)window.FindName("Properties");
        var layout = PropertyChecks.Section(panel, "配置");
        Check(!layout.Children.OfType<CheckBox>().Any(), "Layout section must not expose an enable checkbox");
        var fresh = new SceneObject { Kind = ObjectKind.Shape };
        Call("InitializeObjectLayout", fresh, loaded.Canvas);
        Check(fresh.UseParentLayout && fresh.LayoutSizing == "固定", "New objects must use always-on original size");
        var grid = SceneDocument.CreateGrid("空のグリッド"); loaded.Canvas.Children.Add(grid);
        Call("InitializeObjectLayout", fresh, grid);
        Check(fresh.UseParentLayout && fresh.LayoutSizing == "固定" && fresh.GridColumn == 1, "Grid objects must default to original size");
        var auto = new SceneDocument(); var child = new SceneObject { Kind = ObjectKind.Shape, X = 23, Y = -14 };
        auto.Canvas.Children.Add(child); var before = auto.World(child); auto.EnableParentLayouts();
        Check(child.UseParentLayout && Same(before, auto.World(child)), "Parents without a region must retain ordinary coordinates");
        Set("savedState", loaded.Snapshot());
        Console.WriteLine("PASS: Always-on original size, legacy placement migration, nested/locked objects, no-region fallback and checkbox removal");
    }
}
