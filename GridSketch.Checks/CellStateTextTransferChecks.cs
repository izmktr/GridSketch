using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using GridSketch;

internal static class CellStateTextTransferChecks
{
    public static void Run()
    {
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        var scene = new SceneDocument(); var grid = SceneDocument.CreateGrid("空のグリッド", 3, 2, 48);
        var node = new SceneObject { Kind = ObjectKind.CellSelection, Name = "数字の切り替え" };
        for (int i = 1; i <= 128; i++) node.States.Add(new() { Name = "状態" + i });
        grid.Children.Add(node); scene.Canvas.Children.Add(grid);
        scene.ImportCellStates(node, "01[10]\n9[128]0", GridTextFormat.Compact);
        Check(node.StateCells.Count == 4 && node.StateCells[1].StateId == node.States[9].Id, "Compact state import maps numbers to stable IDs");
        Check(CellStateTextTransfer.Export(node, grid, GridTextFormat.Compact) == "01[10]\r\n9[128]0\r\n", "Compact export uses zero and brackets for multi-digit states");
        var assignments = node.StateCells.Select(c => (c.Column, c.Row, c.StateId)).ToList();
        foreach (var format in Enum.GetValues<GridTextFormat>())
        {
            scene.ImportCellStates(node, CellStateTextTransfer.Export(node, grid, format), format);
            Check(assignments.SequenceEqual(node.StateCells.Select(c => (c.Column, c.Row, c.StateId))), "State round trip: " + format);
        }
        foreach (var input in new[] { "0,1,129\n0,0,0", "0,,1\n0,0,0", "0,-1,1\n0,0,0", "0,a,1\n0,0,0", "0,1,1", "0,1,1\n0,0,999999999999999999999" })
        {
            string before = scene.Snapshot();
            try { scene.ImportCellStates(node, input, GridTextFormat.Csv); throw new Exception("Invalid state input accepted"); }
            catch (FormatException) { }
            Check(scene.Snapshot() == before, "Invalid state input must be atomic");
        }
        var snapshot = SceneDocument.Restore(scene.Snapshot());
        var root = new SceneObject { Kind = ObjectKind.Group, HasLayoutFrame = true, Children = [SceneDocument.CopyPartTemplate(grid)] };
        var part = LibraryPart.FromGroup(root, "状態盤面"); var partScene = new SceneDocument(); partScene.Parts.Add(part);
        var instance = partScene.InsertPart(part, partScene.Canvas); var partNode = partScene.All().Single(n => n.Kind == ObjectKind.CellSelection);
        var partBefore = SceneDocument.Restore(partScene.Snapshot());
        partScene.ImportCellStates(partNode, "000\n000", GridTextFormat.Compact); partScene.CapturePartOverrides(partBefore); partScene.ResolveParts();
        Check(instance.Part!.Overrides[partNode.PartSourcePath!].ContainsKey("StateCells") && partNode.StateCells.Count == 0, "Part state import is an override");

        var window = new MainWindow(); window.Show(); window.UpdateLayout();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Field(string key) => typeof(MainWindow).GetField(key, flags)!.GetValue(window);
        void Set(string key, object value) => typeof(MainWindow).GetField(key, flags)!.SetValue(window, value);
        object? Call(string key, params object?[] args) => typeof(MainWindow).GetMethod(key, flags)!.Invoke(window, args);
        SceneDocument Current() => (SceneDocument)Field("document")!;
        Call("AddProjectTab", scene, null); var tab = Field("activeTab")!;
        Set("selectedId", node.Id); Call("RefreshAll"); Call("OpenGridTextExchange", node);
        var dialog = Application.Current.Windows.OfType<GridTextExchangeWindow>().Single(w => w.Owner == window); dialog.UpdateLayout();
        Check(dialog.Title.StartsWith("マス切り替え") && !ComponentDispatcher.IsThreadModal, "State exchange uses a modeless window");
        var editor = PropertyChecks.Find<TextBox>(dialog, "盤面テキスト"); var formats = PropertyChecks.Find<ComboBox>(dialog, "入出力形式");
        void Click(string name) => PropertyChecks.Find<Button>(dialog, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        formats.SelectedIndex = 2; Check(editor.Text.StartsWith("01[10]"), "State window exports compact numbers");
        var beforeImport = scene.Snapshot(); editor.Text = "000\n[10]00";
        Call("AddProjectTab", new SceneDocument(), null); Click("盤面に反映");
        Check(ReferenceEquals(Field("activeTab"), tab) && Current().Find(node.Id)!.StateCells.Single().StateId == node.States[9].Id, "State import targets original tab");
        Call("Undo"); Check(Current().Snapshot() == beforeImport, "State import undoes in one step");
        Call("Redo"); Click("盤面から読み直す");
        var currentNode = Current().Find(node.Id)!;
        (currentNode.States[0], currentNode.States[1]) = (currentNode.States[1], currentNode.States[0]);
        string reordered = Current().Snapshot(); editor.Text = "111\n111"; Click("盤面に反映");
        Check(Current().Snapshot() == reordered && PropertyChecks.Find<TextBlock>(dialog, "入出力の結果").Text.Contains("並べ替え"), "Changed state order cannot silently change numeric mapping");
        Click("盤面から読み直す"); editor.Text = "111\n111"; Click("盤面に反映");
        Check(Current().Find(node.Id)!.StateCells.All(c => c.StateId == currentNode.States[0].Id) && currentNode.States.Count == 128, "Reload adopts current state order without modifying definitions");
        dialog.Close(); Set("savedState", Current().Snapshot()); window.Close();
        Console.WriteLine("PASS: State TSV/CSV/compact exchange, zero, multi-digit IDs, validation, part overrides, modeless UI, fixed target, undo and reordered state protection");
    }
}
