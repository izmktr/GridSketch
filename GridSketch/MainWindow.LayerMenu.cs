using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace GridSketch;

public partial class MainWindow
{
    private void LayerKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || Keyboard.Modifiers != ModifierKeys.None || e.IsRepeat
            || e.OriginalSource is TextBoxBase or PasswordBox) return;
        e.Handled = true;
        if (SelectedLayers().Count != 1 || Selected == document.Canvas || !document.Editable(Selected)) return;
        DeleteClick(sender, e);
    }

    private void AttachLayerMenu(TreeViewItem item)
    {
        var menu = new ContextMenu();
        item.ContextMenu = menu;
        item.MouseRightButtonDown += (_, e) =>
        {
            ChooseLayer(item, false, false, right: true);
            PopulateLayerMenu(menu);
            e.Handled = true;
        };
        // Prepare before the popup takes focus. Refocusing the tree in Opened closes the menu.
        item.ContextMenuOpening += (_, _) =>
        {
            if (!item.IsSelected) return; // Ignore events bubbling through ancestor items.
            ChooseLayer(item, false, false, right: true);
            PopulateLayerMenu(menu);
        };
    }

    private void PopulateLayerMenu(ContextMenu menu)
    {
        menu.Items.Clear();
        var node = Selected;
        var parent = document.Parent(node);
        bool root = node == document.Canvas, editable = document.Editable(node);
        bool parentEditable = parent is not null && document.Editable(parent);
        void Item(string title, bool enabled, Action action)
        {
            if (SelectedLayers().Count > 1 && title is not ("グループ化" or "親の変更…" or "複製先を選択…")) enabled = false;
            var entry = new MenuItem { Header = title, IsEnabled = enabled, InputGestureText = title switch { "削除" => "Delete", "複製" => "Ctrl+D", "名前の変更…" => "F2", _ => "" } };
            entry.Click += (_, _) => { if (enabled && document.Find(node.Id) is not null) { selectedId = node.Id; Try(action); } };
            menu.Items.Add(entry);
        }
        Item("複製", !root && parentEditable, DuplicateLayer);
        Item("複製先を選択…", document.DuplicateSelectionError(SelectedLayers()) is null, ChooseDuplicateParent);
        Item("削除", !root && editable, () => DeleteClick(this, new RoutedEventArgs()));
        Item("親の変更…", document.ReparentSelectionError(SelectedLayers()) is null, ChooseSelectedParents);
        menu.Items.Add(new Separator());
        Item("名前の変更…", editable, RenameLayer);
        Item("グループ化", GroupSelectionError() is null, () => WrapClick(this, new RoutedEventArgs()));
        Item("グループ解除", node.Kind == ObjectKind.Group && editable && parent?.IsStateSelector != true
            && node.Children.All(c => document.Editable(c)), () => UngroupClick(this, new RoutedEventArgs()));
        menu.Items.Add(new Separator());
        Item(node.Visible ? "非表示にする" : "表示する", !root && parentEditable, () => ToggleProperty(false));
        Item(node.Locked ? "ロック解除" : "ロック", !root && parentEditable, () => ToggleProperty(true));
        menu.Items.Add(new Separator());
        Item("部品として登録…", node.Kind == ObjectKind.Group, () => RegisterPartClick(this, new RoutedEventArgs()));
    }

    private void DuplicateLayer() => Change(() =>
    {
        var node = Selected;
        var parent = document.Parent(node) ?? throw new InvalidOperationException("キャンバスは複製できません。");
        RequireEditable(parent);
        var copy = LibraryPart.Clone(node);
        var stem = node.Name.Length > 180 ? node.Name[..180] : node.Name;
        copy.Name = stem + " のコピー";
        int suffix = 2;
        while (parent.Children.Any(c => c.Name == copy.Name)) copy.Name = stem + " のコピー " + suffix++;
        parent.Children.Insert(parent.Children.IndexOf(node) + 1, copy);
        if (parent.IsStateSelector) SceneDocument.AssignNewState(parent, copy);
        selectedId = copy.Id;
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
