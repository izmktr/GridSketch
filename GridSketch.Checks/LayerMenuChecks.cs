using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using GridSketch;

internal static class LayerMenuChecks
{
    public static void Run(MainWindow window)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        void Check(bool value, string label) { if (!value) throw new Exception(label); }
        var scene = new SceneDocument();
        var group = new SceneObject { Kind = ObjectKind.Group, X = 40, Y = 60, Angle = 30 };
        var shape = new SceneObject { Kind = ObjectKind.Shape, X = 15, Y = 25, Angle = 20 };
        group.Children.Add(shape); scene.Canvas.Children.Add(group);
        Set("document", scene); Set("selectedId", scene.Canvas.Id); Call("RefreshAll");
        var layers = (TreeView)window.FindName("Layers");
        var item = (TreeViewItem)((TreeViewItem)((TreeViewItem)layers.Items[0]).Items[0]).Items[0];
        item.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.MouseRightButtonDownEvent });
        Check((string)typeof(MainWindow).GetField("selectedId", flags)!.GetValue(window)! == shape.Id, "Right click selects the clicked layer without selecting its parent");
        var popup = item.ContextMenu;
        Check(popup.Items.Count > 0, "Context menu is populated before opening");
        window.Activate(); item.Focus();
        popup.PlacementTarget = item; popup.IsOpen = true;
        var frame = new DispatcherFrame();
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        Check(popup.IsOpen && popup.IsKeyboardFocusWithin, "Opened layer menu stays open and retains keyboard focus");
        popup.IsOpen = false;
        ContextMenu Menu(SceneObject node)
        {
            Set("selectedId", node.Id); Call("RefreshAll");
            var menu = new ContextMenu(); Call("PopulateLayerMenu", menu); return menu;
        }
        MenuItem Entry(ContextMenu menu, string title) => menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, title));
        void Click(ContextMenu menu, string title) => Entry(menu, title).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var menu = Menu(shape);
        Check(Entry(menu, "親の変更…").IsEnabled, "Nested shape can move out of parent");
        Click(menu, "複製"); var copy = group.Children[1];
        Check(copy.Id != shape.Id && copy.X == shape.X && copy.Angle == shape.Angle, "Duplicate has independent identity and exact placement");
        copy.Width += 10; Check(copy.Width != shape.Width, "Duplicate content is independent");
        var before = scene.World(shape);
        Menu(shape); Call("Change", new Action(() => scene.ReparentMany(new[] { shape }, scene.Canvas)));
        var after = scene.World(shape);
        Check(scene.Canvas.Children[1] == shape && group.Children.Count == 1, "Move inserts directly below old parent");
        Check(Math.Abs(before.OffsetX - after.OffsetX) < 1e-8 && Math.Abs(before.OffsetY - after.OffsetY) < 1e-8
            && Math.Abs(before.M11 - after.M11) < 1e-8, "Move preserves world position and rotation");
        Click(Menu(shape), "非表示にする"); Check(!shape.Visible, "Menu visibility applies immediately");
        Click(Menu(shape), "ロック"); menu = Menu(shape);
        Check(shape.Locked && !Entry(menu, "削除").IsEnabled && Entry(menu, "ロック解除").IsEnabled, "Locked layer menu supports unlock but disables edits");
        Click(menu, "ロック解除"); Click(Menu(shape), "削除");
        Check(scene.Find(shape.Id) is null, "Delete menu removes target");
        Call("Undo"); scene = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        Check(scene.Find(shape.Id) is not null, "Menu deletion is undoable");
        Menu(scene.Find(shape.Id)!);
        void DeleteKey(UIElement target) => target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
            PresentationSource.FromVisual(window), 0, Key.Delete) { RoutedEvent = Keyboard.KeyDownEvent });
        var field = new TextBox { Text = "abc" };
        ((StackPanel)window.FindName("Properties")).Children.Add(field);
        DeleteKey(field);
        Check(scene.Find(shape.Id) is not null, "Delete in a property editor does not remove a layer");
        DeleteKey(layers);
        Check(scene.Find(shape.Id) is null, "Delete key in the layer tree removes the selected layer");
        Call("Undo"); scene = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        Check(scene.Find(shape.Id) is not null, "Keyboard deletion is undoable");
        var restoredShape = scene.Find(shape.Id)!; restoredShape.Locked = true; Menu(restoredShape); DeleteKey(layers);
        Check(scene.Find(shape.Id) is not null, "Delete key preserves locked layers");
        restoredShape.Locked = false; Menu(scene.Canvas); DeleteKey(layers);
        Check(scene.Canvas.Children.Count > 0, "Delete key preserves the canvas");
        var grid = SceneDocument.CreateGrid("空のグリッド"); scene.Canvas.Children.Add(grid);
        var selector = new SceneObject { Kind = ObjectKind.CellSelection, UseCellLayout = true };
        var template = new SceneObject { Kind = ObjectKind.Group, Children = [new() { Kind = ObjectKind.Shape }] };
        grid.Children.Add(selector); selector.Children.Add(template); SceneDocument.AssignNewState(selector, template);
        SceneDocument.SetCellState(selector, new(0, 0), selector.States[0].Id);
        Click(Menu(grid), "複製");
        var gridCopy = scene.Canvas.Children[scene.Canvas.Children.IndexOf(grid) + 1];
        var stateCopy = gridCopy.Children[0];
        Check(stateCopy.States[0].Id != selector.States[0].Id && stateCopy.States[0].ObjectId == stateCopy.Children[0].Id
            && stateCopy.StateCells[0].StateId == stateCopy.States[0].Id, "Nested template and state references remap when duplicating grid");
        Click(Menu(template), "複製");
        Check(selector.States.Count == 2 && selector.States[1].ObjectId == selector.Children[1].Id, "Duplicating template adds a separate assignment");
        menu = Menu(template);
        Check(!Entry(menu, "グループ解除").IsEnabled && Entry(menu, "親の変更…").IsEnabled && Entry(menu, "部品として登録…").IsEnabled,
            "Template restrictions and part registration reflected in menu");
        menu = Menu(scene.Canvas);
        Check(!Entry(menu, "複製").IsEnabled && !Entry(menu, "削除").IsEnabled && !Entry(menu, "ロック").IsEnabled, "Canvas restrictions reflected in menu");
        Menu(grid);
        var gridItem = ((TreeViewItem)layers.Items[0]).Items.OfType<TreeViewItem>().Single(i => Equals(i.Tag, grid.Id));
        string oldName = grid.Name;
        Call("BeginLayerNameEdit", gridItem);
        var nameEditor = gridItem.Header as TextBox ?? throw new Exception("Inline layer editor did not open");
        window.Activate(); window.UpdateLayout();
        var renameFrame = new DispatcherFrame();
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => renameFrame.Continue = false));
        Dispatcher.PushFrame(renameFrame);
        Check(nameEditor.IsKeyboardFocused && ReferenceEquals(Keyboard.FocusedElement, nameEditor),
            $"Inline layer editor must receive keyboard focus after appearing: loaded={nameEditor.IsLoaded}, header={gridItem.Header.GetType().Name}, focused={Keyboard.FocusedElement?.GetType().Name}, selected={gridItem.IsSelected}, window={window.IsActive}");
        Check(nameEditor.Text == oldName && nameEditor.SelectionLength == oldName.Length, "Inline editor must select the layer name");
        void PressKey(TextBox editor, Key key) => editor.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
            PresentationSource.FromVisual(window)!, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        nameEditor.Text = "新しい名前"; PressKey(nameEditor, System.Windows.Input.Key.Enter);
        Check(grid.Name == "新しい名前" && Equals(gridItem.Header, grid.Label), "Enter must commit inline layer name");
        Call("BeginLayerNameEdit", gridItem); nameEditor = (TextBox)gridItem.Header;
        nameEditor.Text = "取り消す名前"; PressKey(nameEditor, System.Windows.Input.Key.Escape);
        Check(grid.Name == "新しい名前", "Escape must discard inline changes");
        Call("BeginLayerNameEdit", gridItem); nameEditor = (TextBox)gridItem.Header;
        nameEditor.Text = " "; PressKey(nameEditor, System.Windows.Input.Key.Enter);
        Check(grid.Name == "新しい名前", "Empty inline names must preserve the original");
        Call("Undo"); scene = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        Check(scene.Find(grid.Id)!.Name == oldName, "Inline rename must undo in one step");
        grid = scene.Find(grid.Id)!; Menu(grid);
        gridItem = ((TreeViewItem)layers.Items[0]).Items.OfType<TreeViewItem>().Single(i => Equals(i.Tag, grid.Id));
        Call("BeginLayerNameEdit", gridItem);
        ((TextBox)gridItem.Header).Text = "選択変更で確定";
        Call("ChooseLayer", (TreeViewItem)layers.Items[0], false, false, false);
        Check(grid.Name == "選択変更で確定" && gridItem.Header is not TextBox
            && Equals(typeof(MainWindow).GetField("selectedId", flags)!.GetValue(window), scene.Canvas.Id),
            "Selecting another layer must commit the name and remove the inline editor");
        Call("Undo"); scene = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        Check(scene.Find(grid.Id)!.Name == oldName, "Selection-committed rename must remain undoable");
        scene.Validate(); Set("savedState", scene.Snapshot());
        Console.WriteLine("PASS: Layer context menu selection, duplication, state remapping, move, visibility, locks, deletion, restrictions and undo");
    }
}
