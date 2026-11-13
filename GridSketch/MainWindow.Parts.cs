using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GridSketch;

public partial class MainWindow
{
    private Window? partLibraryWindow;
    private Action? refreshPartLibrary;
    private const string PartFilter = "GridSketch部品|*.gspart";
    private string? PartName(Window owner, string current, string title)
    {
        var dialog = new Window { Owner = owner, Title = title, Width = 380, Height = 185, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(16) }; dialog.Content = panel;
        panel.Children.Add(new TextBlock { Text = "部品名（200文字以内）" });
        var name = new TextBox { Text = current, MaxLength = 200 }; panel.Children.Add(name);
        var ok = new Button { Content = "登録", IsDefault = true }; panel.Children.Add(ok);
        ok.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(name.Text)) dialog.DialogResult = true; };
        dialog.Loaded += (_, _) => { name.Focus(); name.SelectAll(); };
        return dialog.ShowDialog() == true ? name.Text.Trim() : null;
    }
    private void RegisterPartClick(object sender, RoutedEventArgs e) => Try(() =>
    {
        var node = Selected;
        if (node.Kind != ObjectKind.Group) throw new InvalidOperationException("登録するグループまたはフレームをレイヤーから選択してください。");
        var name = PartName(this, node.Name, "部品として登録"); if (name is null) return;
        var part = LibraryPart.FromGroup(node, name);
        Change(() => document.Parts.Add(part));
        Status.Text = "部品を登録しました。「部品 → ライブラリ」から再利用できます。";
    });
    private void ImportPartClick(object sender, RoutedEventArgs e) => Try(() => ImportPart(this));
    private string? ImportPart(Window owner)
    {
        var dialog = new OpenFileDialog { Filter = PartFilter, DefaultExt = ".gspart" };
        if (dialog.ShowDialog(owner) != true) return null;
        var part = PartFile.Import(dialog.FileName);
        Change(() => document.Parts.Add(part));
        if (!document.Parts.Any(p => p.Id == part.Id)) return null;
        Status.Text = "部品をインポートしました。ライブラリからコピーを追加できます。";
        return part.Id;
    }
    private void PartLibraryClick(object sender, RoutedEventArgs e)
    {
        if (partLibraryWindow is { } existing)
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate(); return;
        }
        CreatePartLibraryWindow().Show();
    }
    private Window CreatePartLibraryWindow()
    {
        string parentId = AddParent(false).Id, sourceId = Selected.Id, lastSelection = Selected.Id;
        SceneDocument? shownDocument = null;
        var shownParts = new List<(string Id, string Name, SceneObject Template)>();
        bool reloading = false;
        var dialog = new Window { Owner = this, Title = "部品ライブラリ", Width = 760, Height = 570, MinWidth = 620, MinHeight = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        partLibraryWindow = dialog;
        var layout = new DockPanel { Margin = new Thickness(14) }; dialog.Content = layout;
        var help = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(help, Dock.Top); layout.Children.Add(help);
        var actions = new WrapPanel(); DockPanel.SetDock(actions, Dock.Bottom); layout.Children.Add(actions);
        var list = new ListBox { Name = "PartList", HorizontalContentAlignment = HorizontalAlignment.Stretch }; layout.Children.Add(list);
        LibraryPart? SelectedPart() => list.SelectedItem is ListBoxItem { Tag: string id } ? document.Parts.FirstOrDefault(p => p.Id == id) : null;
        void Reload(string? selection = null)
        {
            selection ??= (list.SelectedItem as ListBoxItem)?.Tag as string; list.Items.Clear();
            foreach (var part in activeTab is null ? Enumerable.Empty<LibraryPart>() : document.Parts)
            {
                var scene = new SceneDocument(); scene.Canvas.Children.Add(part.Template);
                var drawing = new SceneRenderer(scene).Drawing;
                var row = new DockPanel { Margin = new Thickness(4) };
                row.Children.Add(new Image { Source = new DrawingImage(drawing), Width = 88, Height = 72, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 12, 0) });
                row.Children.Add(new TextBlock { Text = part.Name, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
                var item = new ListBoxItem { Tag = part.Id, Content = row }; list.Items.Add(item);
                if (part.Id == selection) list.SelectedItem = item;
            }
            if (list.SelectedIndex < 0 && list.Items.Count > 0) list.SelectedIndex = 0;
        }
        Button AddButton(string label, Action action)
        {
            var button = new Button { Content = label }; actions.Children.Add(button);
            button.Click += (_, _) => Try(() => { refreshPartLibrary?.Invoke(); action(); }); return button;
        }
        var insert = AddButton("コピーを追加", () =>
        {
            if (SelectedPart() is not { } part) return;
            Change(() =>
            {
                var parent = document.Find(parentId) ?? throw new InvalidOperationException("追加先がありません。");
                var copy = document.InsertPart(part, parent); selectedId = copy.Id;
                // Automatic selection of a new copy must not nest the next copy inside it.
                lastSelection = copy.Id;
            });
        });
        var edit = AddButton("別タブで編集", () =>
        {
            if (SelectedPart() is not { } part) return;
            EditPartTab(part);
        });
        var import = AddButton("インポート…", () => { var id = ImportPart(dialog); Reload(id); });
        var export = AddButton("エクスポート…", () =>
        {
            if (SelectedPart() is not { } part) return;
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var filename = string.Concat(part.Name.Select(c => invalid.Contains(c) ? '_' : c));
            var file = new SaveFileDialog { Filter = PartFilter, DefaultExt = ".gspart", FileName = filename };
            if (file.ShowDialog(dialog) == true) PartFile.Export(part, file.FileName);
        });
        var rename = AddButton("名前を変更…", () =>
        {
            if (SelectedPart() is not { } part) return;
            var name = PartName(dialog, part.Name, "部品名を変更"); if (name is null) return;
            Change(() => part.Name = name); Reload(part.Id);
        });
        var update = AddButton("選択グループ・フレームで更新…", () =>
        {
            if (SelectedPart() is not { } part || document.Find(sourceId) is not { Kind: ObjectKind.Group } source) return;
            var name = PartName(dialog, part.Name, $"「{source.Name}」で登録内容を更新"); if (name is null) return;
            var replacement = LibraryPart.FromGroup(source, name);
            Change(() => { part.Name = name; part.Template = replacement.Template; }); Reload(part.Id);
        });
        update.ToolTip = "本体で現在選択しているグループまたはフレームで更新します。";
        var delete = AddButton("登録を削除", () => { if (SelectedPart() is { } part) { Change(() => document.Parts.Remove(part)); Reload(); } });
        void UpdateButtons()
        {
            bool selected = activeTab is not null && SelectedPart() is not null;
            import.IsEnabled = activeTab is not null;
            insert.IsEnabled = selected && document.Find(parentId) is { } parent && document.Editable(parent);
            edit.IsEnabled = export.IsEnabled = rename.IsEnabled = delete.IsEnabled = selected;
            update.IsEnabled = selected && document.Find(sourceId)?.Kind == ObjectKind.Group;
        }
        void RefreshLibrary()
        {
            if (reloading) return;
            reloading = true;
            try
            {
                bool changedDocument = !ReferenceEquals(shownDocument, document);
                if (changedDocument || lastSelection != Selected.Id || document.Find(parentId) is null)
                    parentId = AddParent(false).Id;
                sourceId = Selected.Id; lastSelection = Selected.Id;
                var parts = activeTab is null ? [] : document.Parts.Select(p => (p.Id, p.Name, p.Template)).ToList();
                if (changedDocument || !parts.SequenceEqual(shownParts))
                {
                    shownDocument = document; shownParts = parts; Reload();
                }
                string title = activeTab?.Owner is not null ? "部品：" + activeTab.Name
                    : path is null ? "無題" : System.IO.Path.GetFileName(path);
                dialog.Title = activeTab is null ? "部品ライブラリ" : "部品ライブラリ — " + title;
                help.Text = activeTab is null ? "プロジェクトを開くと部品を表示します。"
                    : $"コピーの追加先：{document.Find(parentId)?.Name}\n現在のタブの部品を表示しています。本体で選択すると追加先が変わります。\n登録・更新・削除は配置済みのコピーに影響しません。";
                UpdateButtons();
            }
            finally { reloading = false; }
        }
        list.SelectionChanged += (_, _) => { if (!reloading) UpdateButtons(); };
        AddButton("閉じる", () => dialog.Close());
        refreshPartLibrary = RefreshLibrary;
        dialog.Activated += (_, _) => RefreshLibrary();
        dialog.Closed += (_, _) =>
        {
            if (ReferenceEquals(partLibraryWindow, dialog)) { partLibraryWindow = null; refreshPartLibrary = null; }
        };
        RefreshLibrary(); return dialog;
    }
}
