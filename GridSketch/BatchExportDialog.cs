using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace GridSketch;

public sealed class BatchExportDialog : Window
{
    public IReadOnlyList<BatchExportResult> Results { get; private set; } = [];

    public BatchExportDialog(IReadOnlyList<BatchExportItem> items, string initialFolder)
    {
        Title = "一括エクスポート"; Width = 620; Height = 640; MinWidth = 480; MinHeight = 480;
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
        var selection = new StackPanel { Orientation = Orientation.Horizontal };
        var all = new Button { Content = "すべて選択", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 8, 0) };
        var none = new Button { Content = "すべて解除", Padding = new Thickness(8, 4, 8, 4) };
        selection.Children.Add(all); selection.Children.Add(none); settings.Children.Add(selection);
        var list = new StackPanel(); root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var checks = items.Select((item, index) => new CheckBox { Content = $"{index + 1}. {item.Name}", IsChecked = true,
            Tag = item, Margin = new Thickness(4, 7, 4, 7) }).ToList();
        void Update() => export.IsEnabled = checks.Any(c => c.IsChecked == true) && Directory.Exists(folder.Text);
        foreach (var check in checks)
        {
            AutomationProperties.SetName(check, "出力対象：" + check.Content);
            check.Checked += (_, _) => Update(); check.Unchecked += (_, _) => Update(); list.Children.Add(check);
        }
        all.Click += (_, _) => { foreach (var check in checks) check.IsChecked = true; };
        none.Click += (_, _) => { foreach (var check in checks) check.IsChecked = false; };
        folder.TextChanged += (_, _) => Update(); Update();
        export.Click += (_, _) =>
        {
            try
            {
                Results = BatchExporter.Export(checks.Where(c => c.IsChecked == true).Select(c => (BatchExportItem)c.Tag), folder.Text, (string)format.SelectedItem);
                int count = Results.Count(r => r.Error is null);
                summary.Text = $"{count}件を出力しました。" + (count == Results.Count ? "" : $" {Results.Count - count}件は出力できませんでした。");
                details.Text = string.Join(Environment.NewLine, Results.Select(r => r.Error is null ? "✓ " + r.Path : "× " + r.Name + "：" + r.Error));
                details.Visibility = Visibility.Visible;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { summary.Text = ex.Message; }
        };
    }
}
