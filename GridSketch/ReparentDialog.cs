using System.Windows;
using System.Windows.Controls;

namespace GridSketch;

public sealed class ReparentDialog : Window
{
    public string? SelectedParentId { get; private set; }
    public SceneDocument SelectedDocument { get; private set; }

    public ReparentDialog(SceneDocument document, SceneObject source)
        : this(document, new[] { source }) { }

    public ReparentDialog(SceneDocument document, IReadOnlyList<SceneObject> sources, bool duplicate = false,
        IReadOnlyList<(string Name, SceneDocument Document)>? documents = null)
    {
        SelectedDocument = document;
        var source = sources[0];
        Title = duplicate ? "複製先を選択" : "親を変更"; Width = 460; Height = 560; MinWidth = 360; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        FontFamily = new System.Windows.Media.FontFamily("Yu Gothic UI");
        var root = new DockPanel { Margin = new Thickness(16) }; Content = root;
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        heading.Children.Add(new TextBlock { Text = duplicate ? "複製先の親を選択" : "移動先の親を選択", FontSize = 18, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = "対象：" + (sources.Count == 1 ? source.Label : $"{sources.Count}個のレイヤー"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        string Path(SceneObject node)
        {
            var parts = new List<string>();
            for (SceneObject? current = node; current is not null; current = SelectedDocument.Parent(current)) parts.Add(current.Name);
            parts.Reverse(); return string.Join(" / ", parts);
        }
        heading.Children.Add(new TextBlock { Text = "現在の親：" + (document.Parent(source) is { } parent ? Path(parent) : "なし"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "キャンセル", IsCancel = true, MinWidth = 90 };
        var move = new Button { Content = duplicate ? "複製" : "移動", IsDefault = true, IsEnabled = false, MinWidth = 90 };
        System.Windows.Automation.AutomationProperties.SetName(move, duplicate ? "選択した親へ複製" : "選択した親へ移動");
        move.Click += (_, _) => { if (SelectedParentId is not null) DialogResult = true; };
        actions.Children.Add(cancel); actions.Children.Add(move); DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var selectedPath = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(selectedPath, Dock.Bottom); root.Children.Add(selectedPath);

        var targets = new Dictionary<string, SceneObject>();
        var tree = new TreeView { Padding = new Thickness(6) };
        System.Windows.Automation.AutomationProperties.SetName(tree, duplicate ? "複製先のレイヤー" : "移動先のレイヤー");
        void Add(SceneObject node, ItemsControl destination, string pathPrefix)
        {
            if (targets.ContainsKey(node.Id))
            {
                var item = new TreeViewItem { Header = pathPrefix + node.Label, Tag = node.Id, ToolTip = Path(node), IsExpanded = true };
                destination.Items.Add(item);
                foreach (var child in node.Children) Add(child, item, "");
            }
            else
                foreach (var child in node.Children) Add(child, destination, pathPrefix + node.Name + " / ");
        }
        void Populate()
        {
            SelectedParentId = null; move.IsEnabled = false; selectedPath.Text = "";
            tree.Items.Clear();
            targets = (duplicate ? SelectedDocument.DuplicateTargets(sources, document) : document.ReparentTargets(sources)).ToDictionary(n => n.Id);
            Add(SelectedDocument.Canvas, tree, "");
            if (targets.Count == 0) selectedPath.Text = duplicate ? "複製可能な親がありません。" : "移動可能な親がありません。";
        }
        if (duplicate && documents is { Count: > 1 })
        {
            heading.Children.Add(new TextBlock { Text = "複製先のファイル", Margin = new Thickness(0, 12, 0, 4) });
            var files = new ComboBox();
            System.Windows.Automation.AutomationProperties.SetName(files, "複製先のファイル");
            foreach (var entry in documents)
                files.Items.Add(new ComboBoxItem { Content = entry.Name, Tag = entry.Document });
            files.SelectedItem = files.Items.OfType<ComboBoxItem>().First(i => ReferenceEquals(i.Tag, document));
            files.SelectionChanged += (_, _) =>
            {
                if (files.SelectedItem is ComboBoxItem { Tag: SceneDocument destination })
                {
                    SelectedDocument = destination;
                    Populate();
                }
            };
            heading.Children.Add(files);
        }
        Populate();
        tree.SelectedItemChanged += (_, e) =>
        {
            SelectedParentId = (e.NewValue as TreeViewItem)?.Tag as string;
            move.IsEnabled = SelectedParentId is not null && targets.ContainsKey(SelectedParentId);
            selectedPath.Text = move.IsEnabled ? Path(targets[SelectedParentId!]) : "";
        };
        root.Children.Add(tree);
        Loaded += (_, _) => tree.Focus();
    }
}
