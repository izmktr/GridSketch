using System.Windows;
using System.Windows.Controls;

namespace GridSketch;

public partial class MainWindow
{
    private void AttachLayerMenu(TreeViewItem item)
    {
        item.MouseRightButtonDown += (_, e) =>
        {
            item.IsSelected = true;
            item.Focus();
            e.Handled = true;
        };
        var menu = new ContextMenu();
        item.ContextMenu = menu;
        menu.Opened += (_, _) =>
        {
            item.IsSelected = true;
            PopulateLayerMenu(menu);
        };
    }

    private bool CanMoveLayerToParent(SceneObject node) => node != document.Canvas && !node.IsGridContent
        && document.Editable(node) && document.Parent(node) is { } parent
        && document.Parent(parent) is { } target && document.Editable(target)
        // Template layout uses cell-relative coordinates, not ordinary world coordinates.
        && document.SelectionOwner(node) is null;

    private void PopulateLayerMenu(ContextMenu menu)
    {
        menu.Items.Clear();
        var node = Selected;
        var parent = document.Parent(node);
        bool root = node == document.Canvas, editable = document.Editable(node);
        bool parentEditable = parent is not null && document.Editable(parent);
        void Item(string title, bool enabled, Action action)
        {
            var entry = new MenuItem { Header = title, IsEnabled = enabled };
            entry.Click += (_, _) => { if (enabled && document.Find(node.Id) is not null) { selectedId = node.Id; Try(action); } };
            menu.Items.Add(entry);
        }
        Item("複製", !root && editable, DuplicateLayer);
        Item("削除", !root && editable, () => DeleteClick(this, new RoutedEventArgs()));
        Item("親の階層へ移動", CanMoveLayerToParent(node), MoveLayerToParent);
        menu.Items.Add(new Separator());
        Item("名前の変更…", editable, RenameLayer);
        Item("グループ化", !root && editable && !node.IsGridContent, () => WrapClick(this, new RoutedEventArgs()));
        Item("グループ解除", node.Kind == ObjectKind.Group && editable && parent?.Kind != ObjectKind.CellSelection
            && node.Children.All(c => !c.IsGridContent && document.Editable(c)), () => UngroupClick(this, new RoutedEventArgs()));
        menu.Items.Add(new Separator());
        Item(node.Visible ? "非表示にする" : "表示する", !root && parentEditable, () => ToggleProperty(false));
        Item(node.Locked ? "ロック解除" : "ロック", !root && parentEditable, () => ToggleProperty(true));
        menu.Items.Add(new Separator());
        Item("部品として登録…", node.Kind == ObjectKind.Group, () => RegisterPartClick(this, new RoutedEventArgs()));
    }

    private void DuplicateLayer() => Change(() =>
    {
        var node = Selected; RequireEditable(node);
        var parent = document.Parent(node) ?? throw new InvalidOperationException("キャンバスは複製できません。");
        var copy = LibraryPart.Clone(node);
        var stem = node.Name.Length > 180 ? node.Name[..180] : node.Name;
        copy.Name = stem + " のコピー";
        int suffix = 2;
        while (parent.Children.Any(c => c.Name == copy.Name)) copy.Name = stem + " のコピー " + suffix++;
        parent.Children.Insert(parent.Children.IndexOf(node) + 1, copy);
        if (parent.Kind == ObjectKind.CellSelection) SceneDocument.AssignNewState(parent, copy);
        selectedId = copy.Id;
    });

    private void MoveLayerToParent() => Change(() =>
    {
        var node = Selected;
        if (!CanMoveLayerToParent(node)) throw new InvalidOperationException("このオブジェクトは親の階層へ移動できません。");
        var parent = document.Parent(node)!;
        var target = document.Parent(parent)!;
        document.Reparent(node, target);
        target.Children.Remove(node);
        target.Children.Insert(target.Children.IndexOf(parent) + 1, node);
        target.LayerExpanded = true;
    });

    private void RenameLayer()
    {
        var node = Selected; RequireEditable(node);
        var dialog = new Window { Owner = this, Title = "レイヤー名の変更", Width = 380, Height = 190,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(16) }; dialog.Content = panel;
        panel.Children.Add(new TextBlock { Text = "名前（200文字以内）" });
        var name = new TextBox { Text = node.Name, MaxLength = 200 }; panel.Children.Add(name);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "変更", IsDefault = true };
        var cancel = new Button { Content = "キャンセル", IsCancel = true };
        buttons.Children.Add(ok); buttons.Children.Add(cancel); panel.Children.Add(buttons);
        ok.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(name.Text)) dialog.DialogResult = true; };
        dialog.Loaded += (_, _) => { name.Focus(); name.SelectAll(); };
        if (dialog.ShowDialog() == true) Change(() => { RequireEditable(node); node.Name = name.Text.Trim(); });
    }
}
