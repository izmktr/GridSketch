using System.Windows;
using System.Windows.Controls;

namespace GridSketch;

public sealed class VisibilityPresetDialog : Window
{
    public List<VisibilityPreset> Presets { get; }
    private readonly ListBox list = new();
    private readonly TreeView tree = new();
    private readonly SceneDocument document;

    public VisibilityPresetDialog(SceneDocument source)
    {
        document = source;
        Presets = source.VisibilityPresets.Select(p => new VisibilityPreset { Name = p.Name, HiddenIds = [.. p.HiddenIds] }).ToList();
        Title = "表示プリセットの編集"; Width = 760; Height = 580; MinWidth = 600; MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        FontFamily = new System.Windows.Media.FontFamily("Yu Gothic UI"); FontSize = 14;
        var buttonStyle = new Style(typeof(Button));
        buttonStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(9, 5, 9, 5)));
        buttonStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 4, 5, 0)));
        Resources.Add(typeof(Button), buttonStyle);
        var root = new DockPanel { Margin = new Thickness(16) }; Content = root;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80 };
        ok.Click += (_, _) => DialogResult = true;
        actions.Children.Add(ok); actions.Children.Add(new Button { Content = "キャンセル", IsCancel = true, MinWidth = 90 });
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) }); columns.ColumnDefinitions.Add(new ColumnDefinition()); root.Children.Add(columns);
        var left = new DockPanel { Margin = new Thickness(0, 0, 12, 12) }; columns.Children.Add(left);
        var buttons = new WrapPanel(); DockPanel.SetDock(buttons, Dock.Bottom); left.Children.Add(buttons);
        void Button(string title, Action action) { var b = new Button { Content = title }; b.Click += (_, _) => action(); buttons.Children.Add(b); }
        Button("新規追加…", () =>
        {
            if (Presets.Count >= 128) { MessageBox.Show(this, "表示プリセットは128件までです。"); return; }
            if (AskName(this, "表示プリセットを追加") is not { } name) return;
            var preset = new VisibilityPreset { Name = name }; Presets.Add(preset); Refresh(preset);
        });
        Button("名前変更…", () =>
        {
            if (list.SelectedItem is VisibilityPreset preset && AskName(this, "名前の変更", preset.Name) is { } name)
            { preset.Name = name; Refresh(preset); }
        });
        Button("削除", () =>
        {
            if (list.SelectedItem is not VisibilityPreset preset) return;
            int index = list.SelectedIndex; Presets.Remove(preset); Refresh(Presets.ElementAtOrDefault(Math.Min(index, Presets.Count - 1)));
        });
        void Move(int delta)
        {
            int from = list.SelectedIndex, to = from + delta;
            if (from < 0 || to < 0 || to >= Presets.Count) return;
            var preset = Presets[from]; Presets.RemoveAt(from); Presets.Insert(to, preset); Refresh(preset);
        }
        Button("↑", () => Move(-1)); Button("↓", () => Move(1));
        left.Children.Add(list); list.SelectionChanged += (_, _) => ShowTree();
        var right = new DockPanel { Margin = new Thickness(0, 0, 0, 12) }; Grid.SetColumn(right, 1); columns.Children.Add(right);
        var heading = new TextBlock { Text = "非表示にするオブジェクト", Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(heading, Dock.Top); right.Children.Add(heading); right.Children.Add(tree);
        Refresh(Presets.FirstOrDefault());
    }

    private void Refresh(VisibilityPreset? selected)
    {
        list.ItemsSource = null; list.ItemsSource = Presets; list.SelectedItem = selected; ShowTree();
    }

    private void ShowTree()
    {
        tree.Items.Clear();
        if (list.SelectedItem is not VisibilityPreset preset) return;
        void Add(SceneObject node, ItemsControl parent)
        {
            var check = new CheckBox { Content = $"{node.Name} [{SceneObject.KindName(node.Kind)}]", IsChecked = preset.HiddenIds.Contains(node.Id), Margin = new Thickness(2) };
            check.Checked += (_, _) => preset.HiddenIds.Add(node.Id);
            check.Unchecked += (_, _) => preset.HiddenIds.Remove(node.Id);
            var item = new TreeViewItem { Header = check, IsExpanded = true }; parent.Items.Add(item);
            foreach (var child in node.Children) Add(child, item);
        }
        foreach (var node in document.Canvas.Children) Add(node, tree);
    }

    public static string? AskName(Window owner, string title, string initial = "")
    {
        var dialog = new Window { Owner = owner, Title = title, Width = 380, Height = 190,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        var panel = new StackPanel { Margin = new Thickness(16) }; dialog.Content = panel;
        panel.Children.Add(new TextBlock { Text = "名前（200文字以内）" });
        var name = new TextBox { Text = initial, MaxLength = 200 }; panel.Children.Add(name);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "OK", IsDefault = true, IsEnabled = !string.IsNullOrWhiteSpace(initial) };
        name.TextChanged += (_, _) => ok.IsEnabled = !string.IsNullOrWhiteSpace(name.Text);
        ok.Click += (_, _) => { if (ok.IsEnabled) dialog.DialogResult = true; };
        buttons.Children.Add(ok); buttons.Children.Add(new Button { Content = "キャンセル", IsCancel = true }); panel.Children.Add(buttons);
        dialog.Loaded += (_, _) => { name.Focus(); name.SelectAll(); };
        return dialog.ShowDialog() == true ? name.Text.Trim() : null;
    }
}
