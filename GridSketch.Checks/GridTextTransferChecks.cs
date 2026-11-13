using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GridSketch;

internal static class GridTextTransferChecks
{
    public static void Run(string directory)
    {
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        void Invalid(Action action, string message)
        { try { action(); } catch (FormatException) { return; } throw new Exception(message); }
        var grid = SceneDocument.CreateGrid("空のグリッド", 3, 2, 48);
        var node = new SceneObject { Kind = ObjectKind.GridText, Name = "問題", Glyphs =
            [new() { Column = 0, Row = 0, Text = "1", Font = "Meiryo", Size = 19 },
             new() { Column = 1, Row = 0, Text = "12", InCellText = true }, new() { Column = 2, Row = 0, Text = "あ" },
             new() { Column = 2, Row = 1, Text = "👩‍💻" }] };
        var scene = new SceneDocument(); grid.Children.Add(node); scene.Canvas.Children.Add(grid);
        foreach (var format in Enum.GetValues<GridTextFormat>())
        {
            string output = GridTextTransfer.Export(node, grid, format);
            var cells = GridTextTransfer.Parse(output, format, 3, 2);
            Check(cells[0].SequenceEqual(new[] { "1", "12", "あ" }) && cells[1].SequenceEqual(new[] { "", "", "👩‍💻" }), "Grid text round-trip including empties, multiple characters and graphemes: " + format);
        }
        Check(GridTextTransfer.Export(node, grid, GridTextFormat.Compact) == "1[12]あ\r\n[][]👩‍💻\r\n", "Compact notation uses brackets only for empty and multi-character cells");
        Check(GridTextTransfer.Parse("[a] [é]", GridTextFormat.Compact, 3, 1)[0].SequenceEqual(new[] { "a", " ", "é" }), "Compact input preserves spaces and combining characters");
        var special = new SceneObject { Kind = ObjectKind.GridText, Glyphs = [
            new() { Column = 0, Text = "a,b", InCellText = true }, new() { Column = 1, Text = "a\"b", InCellText = true },
            new() { Column = 2, Text = "[]", InCellText = true }] };
        foreach (var format in new[] { GridTextFormat.Csv, GridTextFormat.Tsv })
            Check(GridTextTransfer.Parse(GridTextTransfer.Export(special, grid, format), format, 3, 2)[0].SequenceEqual(new[] { "a,b", "a\"b", "[]" }), "Delimited formats round-trip quotes, commas and bracket literals");
        Invalid(() => GridTextTransfer.Export(special, grid, GridTextFormat.Compact), "Compact export must reject bracket literals");
        foreach (var input in new[] { "[ab", "a]", "[[a]]", "[a\tb]" })
            Invalid(() => GridTextTransfer.Parse(input, GridTextFormat.Compact, 1, 1), "Invalid compact input accepted");
        foreach (var input in new[] { "\"a", "a\"b", "\"a\"x", "\"a\nb\"", "\"a\tb\"" })
            Invalid(() => GridTextTransfer.Parse(input, GridTextFormat.Csv, 1, 1), "Malformed quotes or controls accepted");
        Invalid(() => GridTextTransfer.Parse("1,2\n3", GridTextFormat.Csv, 2, 2), "Ragged rows accepted");
        Invalid(() => GridTextTransfer.Parse("1,2", GridTextFormat.Csv, 2, 2), "Missing rows accepted");
        Invalid(() => GridTextTransfer.Parse("1,2,3\n4,5,6", GridTextFormat.Csv, 2, 2), "Extra columns accepted");
        foreach (var format in Enum.GetValues<GridTextFormat>())
        {
            var emptyGrid = SceneDocument.CreateGrid("空のグリッド", 1, 2, 48);
            var empty = new SceneObject { Kind = ObjectKind.GridText };
            Check(GridTextTransfer.Parse(GridTextTransfer.Export(empty, emptyGrid, format), format, 1, 2).All(row => row.Single() == ""), "Last empty row preserved in single-column boards");
        }
        Check(GridTextTransfer.Parse("1,2,\r3,4,\r", GridTextFormat.Csv, 3, 2)[1][2] == "", "CR row endings and empty last column supported");
        var original = scene.Snapshot();
        Invalid(() => scene.ImportGridText(node, "too short", GridTextFormat.Tsv), "Mismatched import accepted");
        Check(scene.Snapshot() == original, "Invalid input does not partially mutate the board");
        scene.ImportGridText(node, "2\t\txy\n\t \t\n", GridTextFormat.Tsv);
        Check(node.Glyphs.Count == 3 && node.Glyphs[0].Font == "Meiryo" && node.Glyphs[0].Size == 19
            && node.Glyphs.Single(g => g.Column == 2).InCellText && node.Glyphs.Any(g => g.Text == " "), "Import replaces all text, removes empties and retains cell formatting");
        scene.Validate(); scene = SceneDocument.Restore(original); grid = scene.Find(grid.Id)!; node = scene.Find(node.Id)!;
        var partScene = new SceneDocument();
        var part = LibraryPart.FromGroup(new SceneObject { Kind = ObjectKind.Group, HasLayoutFrame = true, Children = [SceneDocument.CopyPartTemplate(grid)] }, "文字盤面");
        partScene.Parts.Add(part); var instance = partScene.InsertPart(part, partScene.Canvas);
        var partText = partScene.All().Single(n => n.Kind == ObjectKind.GridText);
        var beforePart = SceneDocument.Restore(partScene.Snapshot());
        partScene.ImportGridText(partText, "123\n456", GridTextFormat.Compact); partScene.CapturePartOverrides(beforePart); partScene.ResolveParts();
        Check(instance.Part!.Overrides[partText.PartSourcePath!].ContainsKey("Glyphs")
            && SceneDocument.Restore(partScene.Snapshot()).Find(partText.Id)!.Glyphs.Count == 6
            && part.Template.Children[0].Children[0].Glyphs.Count == 4, "Part imports persist as overrides without changing library defaults");

        var window = new MainWindow(); window.Show(); window.UpdateLayout();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Field(string name) => typeof(MainWindow).GetField(name, flags)!.GetValue(window);
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        object? Call(string name, params object?[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        SceneDocument Current() => (SceneDocument)Field("document")!;
        Call("AddProjectTab", scene, null); var owner = Field("activeTab")!;
        Set("selectedId", node.Id); Call("RefreshAll");
        Call("OpenGridTextExchange", node);
        var dialog = Application.Current.Windows.OfType<GridTextExchangeWindow>().Single(w => w.Owner == window); dialog.UpdateLayout();
        Check(dialog.IsVisible && !ComponentDispatcher.IsThreadModal, "Text exchange is modeless");
        var editor = PropertyChecks.Find<TextBox>(dialog, "盤面テキスト");
        var formatBox = PropertyChecks.Find<ComboBox>(dialog, "入出力形式");
        void Click(string name) => PropertyChecks.Find<Button>(dialog, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        formatBox.SelectedIndex = 2;
        Check(editor.Text.StartsWith("1[12]あ"), "Changing format converts unedited board text");
        editor.Text = "invalid"; var unchanged = Current().Snapshot(); Click("盤面に反映");
        Check(Current().Snapshot() == unchanged && PropertyChecks.Find<TextBlock>(dialog, "入出力の結果").Text.Contains("行数"), "UI rejects malformed input without mutation");
        editor.Text = "9,8,7\n6,5,4"; formatBox.SelectedIndex = 1;
        Check(editor.Text == "9,8,7\n6,5,4", "Format selection preserves pasted text");
        var other = new SceneDocument(); Call("AddProjectTab", other, null);
        Click("盤面に反映");
        Check(ReferenceEquals(Field("activeTab"), owner) && Current().Find(node.Id)!.Glyphs.Count == 6 && other.Canvas.Children.Count == 0,
            "Applying after a tab switch targets the original layer and file");
        Call("Undo"); Check(Current().Snapshot() == unchanged, "Import is a single undo step");
        Call("Redo"); Check(Current().Find(node.Id)!.Glyphs[0].Text == "9", "Import redo restores values");
        Click("盤面から読み直す"); Check(editor.Text.StartsWith("9,8,7"), "Reload follows replaced documents after undo/redo");
        var currentNode = Current().Find(node.Id)!; currentNode.Locked = true;
        var locked = Current().Snapshot(); Click("盤面に反映");
        Check(Current().Snapshot() == locked && !PropertyChecks.Find<Button>(dialog, "盤面に反映").IsEnabled, "Lock changes are checked when applying from an open dialog");
        currentNode.Locked = false; Click("盤面から読み直す");
        editor.Text = "9,8,7\n6,5,4"; dialog.UpdateLayout();
        var preview = new RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, PixelFormats.Pbgra32); preview.Render(dialog);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(preview));
        using (var output = File.Create(Path.Combine(directory, "grid-text-exchange.png"))) png.Save(output);
        Set("savedState", Current().Snapshot()); Call("CloseTabClick", window, new RoutedEventArgs());
        Click("盤面に反映");
        Check(Current() == other && other.Canvas.Children.Count == 0 && PropertyChecks.Find<TextBlock>(dialog, "入出力の結果").Text.Contains("閉じられ"), "Closed target cannot redirect edits into another file");
        dialog.Close(); Set("savedState", Current().Snapshot()); window.Close();
        Console.WriteLine("PASS: Grid text TSV/CSV/compact round-trip, quotes, Unicode, empty cells, strict validation, formatting, modeless UI, stable targets, locks and undo/redo");
    }
}
