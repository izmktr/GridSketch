using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GridSketch;

internal static class WindowChecks
{
    public static void Run(MainWindow window, string directory)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        var layerScene = new SceneDocument(); layerScene.Canvas.Children.Add(SceneDocument.CreateGrid("基本の方眼紙"));
        Set("document", layerScene); Set("selectedId", layerScene.Canvas.Id); Set("savedState", layerScene.Snapshot()); Call("RefreshAll");
        var layers = (TreeView)window.FindName("Layers");
        TreeViewItem Root() => (TreeViewItem)layers.Items[0];
        TreeViewItem GridItem() => (TreeViewItem)Root().Items[0];
        if (!Root().IsExpanded || !GridItem().IsExpanded) throw new Exception("New layers should be expanded");
        GridItem().IsExpanded = false;
        if (layerScene.Canvas.Children[0].LayerExpanded || !layerScene.Canvas.LayerExpanded) throw new Exception("Expansion event modified wrong layer");
        var layerFile = Path.Combine(directory, "layer-state.gridsketch");
        Set("path", layerFile); Call("Save", false);
        GridItem().IsExpanded = true;
        Set("savedState", layerScene.Snapshot()); // Avoid a discard prompt for this deliberate test-only UI change.
        Call("CloseTabClick", window, new RoutedEventArgs()); Call("OpenProject", layerFile);
        if (!Root().IsExpanded || GridItem().IsExpanded) throw new Exception("Saved collapsed grid not restored");
        Root().IsExpanded = false; Call("Save", false); Call("CloseTabClick", window, new RoutedEventArgs()); Call("OpenProject", layerFile);
        if (Root().IsExpanded || GridItem().IsExpanded) throw new Exception("Fully collapsed tree not restored");
        var oldJson = System.Text.Json.Nodes.JsonNode.Parse(layerScene.Snapshot())!;
        void RemoveExpansion(System.Text.Json.Nodes.JsonNode node)
        {
            node.AsObject().Remove("LayerExpanded");
            foreach (var child in node["Children"]!.AsArray()) RemoveExpansion(child!);
        }
        RemoveExpansion(oldJson["Canvas"]!);
        File.WriteAllText(layerFile, oldJson.ToJsonString()); Call("CloseTabClick", window, new RoutedEventArgs()); Call("OpenProject", layerFile);
        if (!Root().IsExpanded || !GridItem().IsExpanded) throw new Exception("Old project did not default to expanded layers");
        Console.WriteLine("PASS: Layer expansion saved and restored, including collapsed root and old-file defaults");
        Set("path", null!);
        var legacy = SceneDocument.Migrate(new Document()); var legacyGrid = legacy.Canvas.Children[0];
        Set("document", legacy); Set("savedState", legacy.Snapshot()); Set("selectedId", legacyGrid.Id); Call("RefreshAll");
        var resizePanel = (StackPanel)window.FindName("Properties");
        PropertyChecks.Find<TextBox>(resizePanel, "列数").Text = "4";
        PropertyChecks.Find<TextBox>(resizePanel, "行数").Text = "3";
        resizePanel.Children.OfType<Button>().Single(b => Equals(b.Content, "プロパティを適用")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (legacyGrid.Columns != 4 || legacyGrid.Rows != 3) throw new Exception("Grid resize property action failed");
        Call("Undo");
        var undone = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        if (undone.Canvas.Children[0].Columns != 16) throw new Exception("Grid resize undo failed");
        Console.WriteLine("PASS: Grid property resize and undo with legacy default borders");
        var typing = new SceneDocument(); var typingGrid = SceneDocument.CreateGrid("基本の方眼紙"); typing.Canvas.Children.Add(typingGrid);
        var textLayer = typingGrid.Children.First(n => n.Kind == ObjectKind.GridText);
        Set("document", typing); Set("selectedId", textLayer.Id); Call("RefreshAll");
        var surface = (SceneSurface)window.FindName("Surface");
        PumpInput();
        Call("SelectTextCell", new CellPosition(0, 0));
        var inputBox = (TextBox)window.FindName("GridInput");
        if (!inputBox.IsKeyboardFocused) throw new Exception("Grid input did not acquire keyboard focus");
        void PumpInput()
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => frame.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
        inputBox.AppendText("a"); PumpInput(); inputBox.AppendText("b"); PumpInput();
        if (textLayer.Glyphs.Count != 2 || !inputBox.IsKeyboardFocused || inputBox.Text.Length != 0) throw new Exception($"Consecutive dispatched text failed: count={textLayer.Glyphs.Count}, focus={inputBox.IsKeyboardFocused}, text={inputBox.Text}, cell={surface.ActiveCell}, visible={inputBox.Visibility}");
        Call("SelectTextCell", new CellPosition(0, 0));
        var composition = new System.Windows.Input.TextComposition(System.Windows.Input.InputManager.Current, inputBox, "あいう");
        inputBox.RaiseEvent(new System.Windows.Input.TextCompositionEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, composition)
            { RoutedEvent = System.Windows.Input.TextCompositionManager.PreviewTextInputStartEvent });
        inputBox.Text = "あい"; PumpInput();
        if (textLayer.Glyphs[0].Text != "a") throw new Exception("Unconfirmed IME text was committed");
        inputBox.RaiseEvent(new System.Windows.Input.TextCompositionEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, composition)
            { RoutedEvent = System.Windows.Input.TextCompositionManager.PreviewTextInputEvent });
        inputBox.Text = "あいう"; PumpInput();
        if (!inputBox.IsKeyboardFocused) throw new Exception("Grid input lost focus after commit");
        if (textLayer.Glyphs.Count != 3 || surface.ActiveCell != new CellPosition(3, 0)) throw new Exception("Direct horizontal commit failed");
        Call("SelectTextCell", new CellPosition(0, 0)); Call("SelectTextCell", new CellPosition(0, 0));
        if (!surface.TextVertical) throw new Exception("Repeated click did not change direction");
        Call("CommitGridInput", "漢字");
        if (textLayer.Glyphs.Single(g => g.Column == 0 && g.Row == 0).Text != "漢" || surface.ActiveCell != new CellPosition(0, 2)) throw new Exception("Vertical overwrite failed");
        Call("EditGridKey", System.Windows.Input.Key.Back);
        if (surface.ActiveCell != new CellPosition(0, 1) || textLayer.Glyphs.Any(g => g.Column == 0 && g.Row == 1)) throw new Exception("Backspace failed");
        Call("SelectTextCell", new CellPosition(typingGrid.Columns - 1, 0));
        var beforeRejectedInput = typing.Snapshot();
        try { Call("CommitGridInput", "AB"); throw new Exception("Overflow accepted"); }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { }
        if (typing.Snapshot() != beforeRejectedInput) throw new Exception("Overflow partially applied");
        Call("CommitGridInput", "終");
        try { Call("CommitGridInput", "次"); throw new Exception("Stopped cursor overwrote last cell"); }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { }
        Call("Undo");
        var typingUndone = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        if (typingUndone.Snapshot() != beforeRejectedInput) throw new Exception("Commit undo failed");
        typingGrid = typingUndone.Canvas.Children[0]; textLayer = typingUndone.Find(textLayer.Id)!;
        typingGrid.Children.Add(new SceneObject { Kind = ObjectKind.Fill, BlockInput = true, FilledCells = [new CellPosition(1, 1)] });
        Call("SelectTextCell", new CellPosition(0, 1)); var beforeBlack = typingUndone.Snapshot();
        try { Call("CommitGridInput", "AB"); throw new Exception("Black cell accepted input"); }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { }
        if (typingUndone.Snapshot() != beforeBlack) throw new Exception("Black cell rejection partially applied");
        Call("SelectTextCell", new CellPosition(0, 0)); Call("EditGridKey", System.Windows.Input.Key.Delete);
        if (surface.ActiveCell != new CellPosition(0, 0) || textLayer.Glyphs.Any(g => g.Column == 0 && g.Row == 0)) throw new Exception("Delete moved cursor or retained text");
        Call("SelectTextCell", new CellPosition(0, 0)); Call("EditGridKey", System.Windows.Input.Key.Right);
        if (!surface.TextVertical || surface.ActiveCell != new CellPosition(1, 0)) throw new Exception("Arrow key changed input direction");
        Console.WriteLine("PASS: Direct grid typing, direction, overwrite, backspace, atomic overflow, stop and undo");
        PumpInput();
        Call("SelectTextCell", new CellPosition(0, 0));
        Call("SelectTextCell", new CellPosition(0, 0));
        Call("SelectTextCell", new CellPosition(0, 0));
        if (!surface.TextInCell || surface.TextVertical) throw new Exception("Third click must select same-cell input");
        inputBox.Text = "1"; PumpInput(); inputBox.Text = "2"; PumpInput();
        var number = textLayer.Glyphs.SingleOrDefault(g => g.Column == 0 && g.Row == 0)
            ?? throw new Exception($"Same-cell input missing: {((TextBlock)window.FindName("Status")).Text}; cell={surface.ActiveCell}; selected={surface.Selected?.Id}; canType={typeof(MainWindow).GetProperty("CanTypeInGrid", flags)!.GetValue(window)}");
        if (number.Text != "12" || !number.InCellText || surface.ActiveCell != new CellPosition(0, 0)) throw new Exception("Same-cell numeric typing failed");
        Set("gridComposing", true); inputBox.Text = "日本"; PumpInput();
        if (number.Text != "12") throw new Exception("Unconfirmed composition appended");
        Set("gridComposing", false); Call("QueueGridInput"); PumpInput();
        if (number.Text != "12日本") throw new Exception("IME commit did not append to the same cell");
        Call("CommitGridInput", "👩‍💻"); Call("EditGridKey", System.Windows.Input.Key.Back);
        if (number.Text != "12日本" || surface.ActiveCell != new CellPosition(0, 0)) throw new Exception("Same-cell backspace split a grapheme or moved the cell");
        Call("CommitGridInput", "1234567890");
        var rendered = new SceneRenderer(typingUndone);
        if (rendered.Bounds[textLayer.Id].Left >= typingUndone.World(typingGrid).OffsetX) throw new Exception("Same-cell text was clipped or shrunk");
        var multiPath = Path.Combine(directory, "multi-cell-text.gridsketch"); typingUndone.Save(multiPath);
        var reloadedText = SceneDocument.Load(multiPath).Find(textLayer.Id)!.Glyphs.Single(g => g.Column == 0 && g.Row == 0);
        if (reloadedText.Text != number.Text || !reloadedText.InCellText) throw new Exception("Multi-character cell did not survive saving");
        Call("EditGridKey", System.Windows.Input.Key.Delete);
        if (textLayer.Glyphs.Any(g => g.Column == 0 && g.Row == 0)) throw new Exception("Same-cell Delete failed");
        Call("Undo");
        var multiUndone = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        if (multiUndone.Find(textLayer.Id)!.Glyphs.Single(g => g.Column == 0 && g.Row == 0).Text != number.Text) throw new Exception("Same-cell undo failed");
        Call("SelectTextCell", new CellPosition(0, 0));
        if (surface.TextInCell || surface.TextVertical) throw new Exception("Fourth click must return to rightward input");
        Console.WriteLine("PASS: Same-cell typing, IME commit, grapheme deletion, overflow rendering, save and undo");
        var scene = SceneDocument.Load(Path.Combine(directory, "objects-sample.gridsketch"));
        Set("document", scene); Set("savedState", scene.Snapshot()); Set("selectedId", scene.Canvas.Id); Call("RefreshAll");
        var grid = scene.Canvas.Children.Single(n => n.Kind == ObjectKind.Grid);
        foreach (var kind in Enum.GetValues<ObjectKind>())
        {
            var node = kind == ObjectKind.Canvas ? scene.Canvas : new SceneObject { Kind = kind, Name = "プロパティ確認" };
            if (node != scene.Canvas) grid.Children.Add(node);
            Set("selectedId", node.Id); Call("RefreshAll"); window.UpdateLayout();
            var properties = (StackPanel)window.FindName("Properties");
            if (properties.Children.Count < 4) throw new Exception("Missing properties for " + kind);
            if (node != scene.Canvas) grid.Children.Remove(node);
        }
        Set("selectedId", grid.Id); Call("RefreshAll");
        var before = scene.Snapshot(); Call("Change", (Action)(() => grid.X += 50)); Call("Undo");
        var current = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        if (current.Snapshot() != before) throw new Exception("Window undo failed");
        Call("Redo"); current = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        if (current.Find(grid.Id)!.X != 50) throw new Exception("Window redo failed");
        Call("Undo");
        Set("savedState", before); Set("selectedId", scene.Canvas.Id); Call("RefreshAll");
        Call("CenterClick", window, new RoutedEventArgs()); window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(directory, "window-preview.png")); encoder.Save(file);
        Console.WriteLine("PASS: All property panels, window undo/redo and rendered layout");
    }
}

