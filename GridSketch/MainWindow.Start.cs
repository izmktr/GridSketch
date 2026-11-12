using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GridSketch;

public partial class MainWindow
{
    private StackPanel? startRecent;

    private void ShowStartScreen()
    {
        Keyboard.ClearFocus(); propertyHelpWindow?.Close(); propertyHelpOpen = false;
        document = new SceneDocument(); path = null; selectedId = document.Canvas.Id; savedState = document.Snapshot();
        undo.Clear(); redo.Clear(); BreakUndoMerge(); layerSelection.Clear(); layerAnchor = null;
        Surface.Selected = null; Surface.ActiveCell = Surface.RangeEnd = null; Surface.Preview = [];
        clearingGridInput = true; GridInput.Clear(); clearingGridInput = false; GridInput.Visibility = Visibility.Collapsed;
        Layers.Items.Clear(); propertySectionSummaries.Clear(); Properties.Children.Clear();
        BuildStartScreen(); UpdateTabHeaders(); Title = "GridSketch";
        Status.Text = "新しく作成するか、プロジェクトを開いてください。";
        CommandManager.InvalidateRequerySuggested();
    }

    private void UpdateStartScreen()
    {
        bool opened = activeTab is not null;
        StartScreen.Visibility = opened ? Visibility.Collapsed : Visibility.Visible;
        EditorArea.Visibility = EditorToolbar.Visibility = TabStrip.Visibility = opened ? Visibility.Visible : Visibility.Collapsed;
        foreach (var menu in new[] { CloseDocumentMenu, SaveDocumentMenu, SaveAllDocumentMenu, SaveAsDocumentMenu, ExportDocumentMenu,
            ExportSettingsMenu, EditDocumentMenu, AddDocumentMenu, PartsDocumentMenu }) menu.IsEnabled = opened;
    }

    private void BuildStartScreen()
    {
        StartContent.Children.Clear();
        StartContent.Children.Add(new TextBlock { Text = "GridSketch", FontSize = 32, FontWeight = FontWeights.SemiBold, Foreground = Brushes.DarkSlateBlue });
        StartContent.Children.Add(new TextBlock { Text = "方眼紙から、パズルや図を作る。", FontSize = 16, Margin = new Thickness(0, 8, 0, 28), Foreground = Brushes.SlateGray });
        StartContent.Children.Add(new TextBlock { Text = "新しく作成", FontSize = 19, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        var cards = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
        StartContent.Children.Add(cards);
        foreach (var (title, description, preset) in new[] {
            ("方眼紙", "つながった罫線を基準に作成", "基本の方眼紙"),
            ("マス目", "余白のある独立したマスで作成", "独立マス"),
            ("空のキャンバス", "画像や文字を自由に配置", "キャンバスのみ") })
        {
            var content = new StackPanel();
            var drawing = new Canvas { Width = 180, Height = 86, Margin = new Thickness(0, 0, 0, 16), IsHitTestVisible = false };
            if (preset == "キャンバスのみ")
            {
                var paper = new Rectangle { Width = 106, Height = 80, Stroke = Brushes.SlateGray, Fill = Brushes.White, StrokeThickness = 1 };
                Canvas.SetLeft(paper, 37); drawing.Children.Add(paper);
            }
            else for (int y = 0; y < 3; y++) for (int x = 0; x < 6; x++)
            {
                var cell = new Rectangle { Width = preset == "独立マス" ? 23 : 28, Height = preset == "独立マス" ? 23 : 28,
                    Stroke = new SolidColorBrush(Color.FromRgb(80, 120, 160)), Fill = x == 2 && y == 1 ? Brushes.LightGoldenrodYellow : Brushes.White, StrokeThickness = 1 };
                Canvas.SetLeft(cell, x * 28); Canvas.SetTop(cell, y * 28); drawing.Children.Add(cell);
            }
            content.Children.Add(drawing);
            content.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold });
            content.Children.Add(new TextBlock { Text = description, Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
            var button = new Button { Content = content, Padding = new Thickness(18), Margin = new Thickness(0, 0, 12, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = Brushes.White };
            System.Windows.Automation.AutomationProperties.SetName(button, title + "を新規作成");
            button.Click += (_, _) => Try(() => { var canvas = GridDialog(true, preset); if (canvas is not null) AddProjectTab(new SceneDocument { Canvas = canvas }); });
            cards.Children.Add(button);
        }
        var open = new Button { Content = "ファイルを開く…", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(20, 8, 20, 8), Margin = new Thickness(0, 22, 0, 24) };
        open.Click += OpenClick; StartContent.Children.Add(open);
        StartContent.Children.Add(new TextBlock { Text = "最近使ったファイル", FontSize = 19, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        startRecent = new StackPanel(); StartContent.Children.Add(startRecent); RefreshStartRecent();
        StartContent.Children.Add(new TextBlock { Text = ".gridsketch ファイルをここにドロップして開く", Foreground = Brushes.SlateGray, Margin = new Thickness(0, 24, 0, 8) });
    }

    private void RefreshStartRecent()
    {
        if (startRecent is null) return;
        startRecent.Children.Clear();
        if (recentFiles.Entries.Count == 0) startRecent.Children.Add(new TextBlock { Text = "最近使ったファイルはありません。", Foreground = Brushes.SlateGray });
        foreach (var file in recentFiles.Entries)
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = System.IO.Path.GetFileName(file), FontSize = 15 });
            content.Children.Add(new TextBlock { Text = file, Foreground = Brushes.SlateGray, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
            var button = new Button { Content = content, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 12, 6), ToolTip = file };
            button.Click += (_, _) => Try(() =>
            {
                try { OpenProject(file); }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                { recentFiles.Remove(file); RefreshRecentFilesMenu(); throw new FileNotFoundException("ファイルが見つかりません。履歴から削除しました。\n" + file, ex); }
            });
            startRecent.Children.Add(button);
        }
    }

    private static string[] DroppedProjects(IDataObject data) => data.GetData(DataFormats.FileDrop) is string[] files
        ? files.Where(f => string.Equals(System.IO.Path.GetExtension(f), ".gridsketch", StringComparison.OrdinalIgnoreCase)
            && !Directory.Exists(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : [];
    private void ProjectFilesDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Effects = DroppedProjects(e.Data).Length > 0 ? e.AllowedEffects & DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private void OpenDroppedProjects(IDataObject data)
    {
        CommitPropertyEdit(); FinishDrag();
        foreach (var file in DroppedProjects(data)) Try(() => OpenProject(file));
    }
    private void ProjectFilesDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Handled = true;
        OpenDroppedProjects(e.Data);
    }
}
