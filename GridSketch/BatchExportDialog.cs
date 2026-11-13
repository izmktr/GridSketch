using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace GridSketch;

public sealed class BatchExportDialog : Window
{
    public IReadOnlyList<BatchExportResult> Results { get; private set; } = [];

    public BatchExportDialog(IReadOnlyList<BatchExportItem> items, string initialFolder)
    {
        Title = "一括エクスポート"; Width = 680; Height = 760; MinWidth = 540; MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(18) }; Content = root;
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var summary = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 6) };
        AutomationProperties.SetName(summary, "一括出力結果"); footer.Children.Add(summary);
        var details = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 130,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Visibility = Visibility.Collapsed };
        AutomationProperties.SetName(details, "一括出力の詳細"); footer.Children.Add(details);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var export = new Button { Content = "選択したタブを出力", IsDefault = true, Padding = new Thickness(12, 6, 12, 6) };
        var close = new Button { Content = "閉じる", IsCancel = true, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(8, 0, 0, 0) };
        AutomationProperties.SetName(export, "選択したタブを出力");
        buttons.Children.Add(export); buttons.Children.Add(close); footer.Children.Add(buttons);
        var settings = new StackPanel(); DockPanel.SetDock(settings, Dock.Top); root.Children.Add(settings);
        settings.Children.Add(new TextBlock { Text = "出力形式" });
        var format = new ComboBox { ItemsSource = new[] { "PNG", "PDF" }, SelectedIndex = 0, Margin = new Thickness(0, 4, 0, 12) };
        AutomationProperties.SetName(format, "一括出力形式"); settings.Children.Add(format);
        settings.Children.Add(new TextBlock { Text = "出力先フォルダ" });
        var folderRow = new DockPanel { Margin = new Thickness(0, 4, 0, 10) };
        var browse = new Button { Content = "参照…", Padding = new Thickness(10, 4, 10, 4) };
        DockPanel.SetDock(browse, Dock.Right); folderRow.Children.Add(browse);
        var folder = new TextBox { Text = initialFolder, VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(folder, "一括出力先フォルダ"); folderRow.Children.Add(folder); settings.Children.Add(folderRow);
        browse.Click += (_, _) =>
        {
            var picker = new OpenFolderDialog { Title = "一括エクスポートの出力先", Multiselect = false };
            if (Directory.Exists(folder.Text)) picker.InitialDirectory = folder.Text;
            if (picker.ShowDialog(this) == true) folder.Text = picker.FolderName;
        };
        settings.Children.Add(new TextBlock { Text = "各タブの現在の内容を、タブごとのキャンバス・PDF設定で出力します。\n同名ファイルがある場合は番号を付けて保存します。", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 12) });
        var mode = new ComboBox { ItemsSource = new[] { "現在の表示", "表示プリセット" }, SelectedIndex = 0 };
        AutomationProperties.SetName(mode, "一括出力の表示方法"); settings.Children.Add(mode);
        var presetButton = new ToggleButton { Content = "プリセットを選択 ▼", HorizontalContentAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 5, 0, 5), Padding = new Thickness(8, 5, 8, 5), Visibility = Visibility.Collapsed };
        AutomationProperties.SetName(presetButton, "出力プリセットを選択"); settings.Children.Add(presetButton);
        var presetList = new StackPanel();
        var popup = new Popup { PlacementTarget = presetButton, Placement = PlacementMode.Bottom, StaysOpen = false,
            Child = new Border { Background = Brushes.White, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Padding = new Thickness(8),
                Child = new ScrollViewer { Content = presetList, MaxHeight = 240, MinWidth = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } } };
        popup.SetBinding(Popup.IsOpenProperty, new Binding(nameof(ToggleButton.IsChecked)) { Source = presetButton, Mode = BindingMode.TwoWay });
        AutomationProperties.SetName(popup, "出力プリセット候補");
        settings.Children.Add(popup);
        var selectedPresets = new HashSet<string>(StringComparer.Ordinal);
        var planned = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 8) };
        AutomationProperties.SetName(planned, "一括出力予定"); settings.Children.Add(planned);
        var selection = new StackPanel { Orientation = Orientation.Horizontal };
        var all = new Button { Content = "すべて選択", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 8, 0) };
        var none = new Button { Content = "すべて解除", Padding = new Thickness(8, 4, 8, 4) };
        selection.Children.Add(all); selection.Children.Add(none); settings.Children.Add(selection);
        var list = new StackPanel(); root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var checks = items.Select((item, index) => new CheckBox { Content = $"{index + 1}. {item.Name}", IsChecked = true,
            Tag = item, Margin = new Thickness(4, 7, 4, 7) }).ToList();
        var rowNotes = checks.ToDictionary(c => c, _ => new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(24, 0, 4, 5) });
        List<BatchExportItem> SelectedItems() => checks.Where(c => c.IsChecked == true).Select(c => (BatchExportItem)c.Tag).ToList();
        BatchExportPlan Plan() => mode.SelectedIndex == 0 ? new(SelectedItems(), []) : BatchExporter.Plan(SelectedItems(), selectedPresets);
        void Update()
        {
            var plan = Plan();
            export.IsEnabled = plan.Jobs.Count > 0 && Directory.Exists(folder.Text);
            planned.Text = $"{plan.Jobs.Count}件出力・{plan.Excluded.Count}件除外";
            if (mode.SelectedIndex == 1 && selectedPresets.Count == 0) planned.Text += " — プリセットを選択してください。";
            presetButton.Content = (selectedPresets.Count == 0 ? "プリセットを選択" : string.Join("・", selectedPresets)) + " ▼";
            foreach (var check in checks)
            {
                var item = (BatchExportItem)check.Tag;
                rowNotes[check].Text = check.IsChecked != true ? "対象外" : mode.SelectedIndex == 0 ? "現在の表示を出力" :
                    string.Join(" ／ ", selectedPresets.Select(name =>
                    {
                        int count = item.Document.VisibilityPresets.Count(p => p.Name == name);
                        return name + (count == 1 ? " ✓" : count == 0 ? "：なし・除外" : "：同名が複数・除外");
                    }));
            }
        }
        void UpdateCandidates()
        {
            var selected = SelectedItems();
            var names = selected.SelectMany(i => i.Document.VisibilityPresets).Select(p => p.Name).Distinct(StringComparer.Ordinal).ToList();
            selectedPresets.IntersectWith(names);
            presetList.Children.Clear();
            foreach (var name in names)
            {
                int count = selected.Count(i => i.Document.VisibilityPresets.Count(p => p.Name == name) == 1);
                var check = new CheckBox { Content = $"{name} — {count}/{selected.Count}タブ", IsChecked = selectedPresets.Contains(name), Margin = new Thickness(4, 6, 4, 6) };
                AutomationProperties.SetName(check, "出力プリセット：" + name);
                check.Checked += (_, _) => { selectedPresets.Add(name); Update(); };
                check.Unchecked += (_, _) => { selectedPresets.Remove(name); Update(); };
                presetList.Children.Add(check);
            }
            if (names.Count == 0) presetList.Children.Add(new TextBlock { Text = "選択したタブには表示プリセットがありません。" });
            Update();
        }
        foreach (var check in checks)
        {
            AutomationProperties.SetName(check, "出力対象：" + check.Content);
            check.Checked += (_, _) => UpdateCandidates(); check.Unchecked += (_, _) => UpdateCandidates();
            list.Children.Add(check); list.Children.Add(rowNotes[check]);
        }
        all.Click += (_, _) => { foreach (var check in checks) check.IsChecked = true; };
        none.Click += (_, _) => { foreach (var check in checks) check.IsChecked = false; };
        mode.SelectionChanged += (_, _) => { presetButton.Visibility = mode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed; popup.IsOpen = false; Update(); };
        folder.TextChanged += (_, _) => Update(); UpdateCandidates();
        export.Click += (_, _) =>
        {
            try
            {
                var plan = Plan();
                Results = BatchExporter.Export(plan.Jobs, folder.Text, (string)format.SelectedItem);
                int count = Results.Count(r => r.Error is null);
                summary.Text = $"{count}件を出力しました。{plan.Excluded.Count}件除外。" + (count == Results.Count ? "" : $" {Results.Count - count}件は出力できませんでした。");
                details.Text = string.Join(Environment.NewLine, Results.Select(r => r.Error is null ? "✓ " + r.Path : "× " + r.Name + "：" + r.Error).Concat(plan.Excluded.Select(s => "− " + s)));
                details.Visibility = Visibility.Visible;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { summary.Text = ex.Message; }
        };
    }
}
