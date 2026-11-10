using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GridSketch;

internal static class VisibilityPresetChecks
{
    public static void Run(MainWindow window, string directory)
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        var scene = new SceneDocument();
        var group = new SceneObject { Kind = ObjectKind.Group, Visible = false };
        var child = new SceneObject { Kind = ObjectKind.Shape, Locked = true };
        var highlight = new SceneObject { Kind = ObjectKind.Shape, Visible = false };
        group.Children.Add(child); scene.Canvas.Children.AddRange([group, highlight]);
        var preset = scene.CaptureVisibility("問題のみ"); scene.VisibilityPresets.Add(preset);
        Check(preset.HiddenIds.SetEquals([group.Id, highlight.Id]), "Capture excludes children merely hidden by their parent");
        scene.ApplyVisibility(null); Check(scene.All().All(n => n.Visible), "Show all restores every layer");
        var added = new SceneObject { Kind = ObjectKind.Shape, Visible = false }; scene.Canvas.Children.Add(added);
        preset.HiddenIds.Add(child.Id); scene.ApplyVisibility(preset);
        Check(!group.Visible && !child.Visible && !highlight.Visible && added.Visible, "Preset affects locked layers and shows newly added objects");
        string file = Path.Combine(directory, "visibility-presets.gridsketch"); scene.Save(file);
        var loaded = SceneDocument.Load(file);
        Check(loaded.VisibilityPresets[0].HiddenIds.SetEquals(preset.HiddenIds), "Presets persist with projects");
        string beforeDelete = scene.Snapshot(); scene.Canvas.Children.Remove(group); scene.Validate();
        Check(!preset.HiddenIds.Contains(group.Id) && !preset.HiddenIds.Contains(child.Id), "Deleting a subtree removes all preset references");
        scene = SceneDocument.Restore(beforeDelete); scene.Validate();
        Check(scene.VisibilityPresets[0].HiddenIds.Contains(child.Id), "Undo snapshot restores deleted preset references");
        var editor = new VisibilityPresetDialog(scene);
        editor.Owner = window; editor.Show(); editor.UpdateLayout();
        var preview = new RenderTargetBitmap((int)editor.ActualWidth, (int)editor.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        preview.Render(editor); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(preview));
        using (var output = File.Create(Path.Combine(directory, "visibility-presets.png"))) encoder.Save(output);
        editor.Presets[0].HiddenIds.Clear(); editor.Presets[0].Name = "下書き";
        Check(scene.VisibilityPresets[0].Name == "問題のみ" && scene.VisibilityPresets[0].HiddenIds.Count == 3, "Editor draft preserves document until accepted");
        editor.Close();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MainWindow).GetField("document", flags)!.SetValue(window, scene);
        typeof(MainWindow).GetField("selectedId", flags)!.SetValue(window, scene.Canvas.Id);
        typeof(MainWindow).GetMethod("RefreshAll", flags)!.Invoke(window, null);
        string beforeApply = scene.Snapshot();
        typeof(MainWindow).GetMethod("Change", flags)!.Invoke(window, [new Action(() => scene.ApplyVisibility(null))]);
        typeof(MainWindow).GetMethod("Undo", flags)!.Invoke(window, null);
        var undone = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        Check(undone.Snapshot() == beforeApply, "Preset application is a single undoable operation");
        var grid = SceneDocument.CreateGrid("基本の方眼紙");
        var text = grid.Children.Single(n => n.Kind == ObjectKind.GridText);
        undone.Canvas.Children.Add(grid);
        typeof(MainWindow).GetField("selectedId", flags)!.SetValue(window, text.Id);
        var surface = (SceneSurface)window.FindName("Surface");
        var canType = typeof(MainWindow).GetProperty("CanTypeInGrid", flags)!;
        var mode = typeof(MainWindow).GetField("cellEditMode", flags)!;
        var previousMode = mode.GetValue(window);
        mode.SetValue(window, true);
        typeof(MainWindow).GetMethod("RefreshAll", flags)!.Invoke(window, null);
        surface.ActiveCell = new CellPosition(0, 0);
        Check((bool)canType.GetValue(window)!, "Visible grid text accepts input");
        grid.Visible = false;
        Check(!(bool)canType.GetValue(window)!, "Grid text under a hidden parent rejects keyboard input");
        typeof(MainWindow).GetMethod("SurfaceDown", flags)!.Invoke(window,
            [surface, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)]);
        Check(((TextBlock)window.FindName("Status")).Text == "表示してからマス・辺を編集してください。", "Hidden grid content rejects canvas editing");
        mode.SetValue(window, previousMode);
        Console.WriteLine("PASS: Visibility presets capture, apply, locks, new/deleted layers, persistence, draft cancellation and undo");
    }
}
