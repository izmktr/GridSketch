using Microsoft.Win32;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GridSketch;

public partial class MainWindow : Window
{
    private Document document = new();
    private string? path;
    private bool dirty;
    private readonly RecentFiles recentFiles = new();
    public MainWindow()
    {
        InitializeComponent();
        FontBox.ItemsSource = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(f => f).ToList();
        FontBox.Text = "Yu Gothic";
        StrokeTarget.SelectedIndex = 0;
        ShowDocument();
        RefreshRecentFilesMenu();
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, (_, _) => Save(false)));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Open, (_, _) => OpenClick(this, new RoutedEventArgs())));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.New, (_, _) => NewClick(this, new RoutedEventArgs())));
    }
    private void Try(Action action)
    {
        try { action(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "操作を完了できませんでした", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private static double Number(TextBox box) => double.Parse(box.Text, CultureInfo.CurrentCulture);
    private void UpdateTitle() => Title = $"{(dirty ? "* " : "")}{(path is null ? "無題" : Path.GetFileName(path))} — GridSketch";
    private void Changed()
    {
        dirty = true;
        Surface.Refresh(true);
        UpdateTitle();
        Status.Text = "変更しました。Ctrl+Sで保存できます。";
    }
    private void ShowDocument()
    {
        Surface.Document = document;
        Surface.Selected = null;
        Surface.HoveredEdge = null;
        WallColorBox.Text = document.WallStroke.Color; WallWidthBox.Text = document.WallStroke.Width.ToString();
        ColumnsBox.Text = document.Columns.ToString(); RowsBox.Text = document.Rows.ToString();
        SizeBox.Text = document.CellSize.ToString(); MarginBox.Text = document.Margin.ToString();
        ShowStroke(); ShowCell(); Surface.Refresh(true); UpdateTitle();
    }
    private void ShowStroke()
    {
        if (ColorBox is null || StrokeTarget.SelectedIndex < 0) return;
        var stroke = document.Strokes[StrokeTarget.SelectedIndex];
        ColorBox.Text = stroke.Color; WidthBox.Text = stroke.Width.ToString();
        KindBox.SelectedIndex = stroke.Kind switch { "破線" => 1, "点線" => 2, _ => 0 };
    }
    private void StrokeTargetChanged(object sender, SelectionChangedEventArgs e) => ShowStroke();
    private void ApplyStrokeClick(object sender, RoutedEventArgs e) => Try(() =>
    {
        var stroke = new Stroke { Color = ColorBox.Text, Width = Number(WidthBox), Kind = ((ComboBoxItem)KindBox.SelectedItem).Content.ToString()! };
        var candidate = new Document { Strokes = [stroke, stroke, stroke, stroke] };
        candidate.Validate();
        document.Strokes[StrokeTarget.SelectedIndex] = stroke; Changed();
    });
    private void ApplyGridClick(object sender, RoutedEventArgs e) => Try(() =>
    {
        var candidate = new Document { Columns = int.Parse(ColumnsBox.Text), Rows = int.Parse(RowsBox.Text), CellSize = Number(SizeBox), Margin = Number(MarginBox) };
        candidate.Validate();
        if (document.Cells.Any(c => c.Column >= candidate.Columns || c.Row >= candidate.Rows))
            throw new InvalidOperationException("縮小後の範囲外に配置があります。該当するマスの内容を先に削除してください。");
        if (document.Walls.Any(w => !w.IsInside(candidate.Columns, candidate.Rows)))
            throw new InvalidOperationException("縮小後の範囲外に壁があります。該当する壁を先に解除してください。");
        document.Columns = candidate.Columns; document.Rows = candidate.Rows; document.CellSize = candidate.CellSize; document.Margin = candidate.Margin;
        Surface.Selected = null; ShowCell(); Changed();
    });
    private void SurfaceClick(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(Surface);
        if (EditMode.SelectedIndex == 1)
        {
            if (Surface.EdgeAt(p, Zoom.Value) is { } edge)
            {
                bool added = document.ToggleWall(edge);
                Surface.HoveredEdge = null;
                Changed();
                Status.Text = added ? "壁を追加しました。同じ辺をクリックすると解除できます。" : "壁を解除して元の罫線に戻しました。";
            }
            return;
        }
        int c = (int)Math.Floor((p.X - Surface.PaddingSize) / document.CellSize), r = (int)Math.Floor((p.Y - Surface.PaddingSize) / document.CellSize);
        if (c < 0 || r < 0 || c >= document.Columns || r >= document.Rows) return;
        Surface.Selected = (c, r); Surface.InvalidateVisual(); ShowCell();
        Status.Text = $"{c + 1}列・{r + 1}行を選択中";
    }
    private void EditModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Surface is null) return;
        Surface.Selected = null; Surface.HoveredEdge = null;
        Surface.Cursor = EditMode.SelectedIndex == 1 ? Cursors.Cross : Cursors.Arrow;
        ShowCell(); Surface.InvalidateVisual();
        Status.Text = EditMode.SelectedIndex == 1 ? "マスの辺をクリックして壁を追加・解除してください。" : "マスをクリックして選択してください。";
    }
    private void SurfaceMove(object sender, MouseEventArgs e)
    {
        var edge = EditMode.SelectedIndex == 1 ? Surface.EdgeAt(e.GetPosition(Surface), Zoom.Value) : null;
        if (edge == Surface.HoveredEdge) return;
        Surface.HoveredEdge = edge; Surface.InvalidateVisual();
    }
    private void SurfaceLeave(object sender, MouseEventArgs e)
    {
        Surface.HoveredEdge = null; Surface.InvalidateVisual();
    }
    private void ApplyWallClick(object sender, RoutedEventArgs e) => Try(() =>
    {
        var stroke = new Stroke { Color = WallColorBox.Text, Width = Number(WallWidthBox) };
        new Document { WallStroke = stroke }.Validate();
        document.WallStroke = stroke; Surface.HoveredEdge = null; Changed();
    });
    private Cell? SelectedCell() => Surface.Selected is { } s ? document.Cells.Find(c => c.Column == s.Column && c.Row == s.Row) : null;
    private Cell RequireCell()
    {
        if (Surface.Selected is not { } s) throw new InvalidOperationException("先にマスをクリックしてください。");
        var cell = SelectedCell();
        if (cell is null) { cell = new Cell { Column = s.Column, Row = s.Row }; document.Cells.Add(cell); }
        return cell;
    }
    private void ShowCell()
    {
        CellTitle.Text = Surface.Selected is { } s ? $"{s.Column + 1}列・{s.Row + 1}行" : "マスを選択";
        var cell = SelectedCell(); TextBox.Text = cell?.Text ?? "";
        if (cell is not null) { FontBox.Text = cell.Font; FontSizeBox.Text = cell.FontSize.ToString(); }
    }
    private void TextClick(object sender, RoutedEventArgs e) => Try(() =>
    {
        if (new StringInfo(TextBox.Text).LengthInTextElements != 1 || TextBox.Text.Any(char.IsControl)) throw new InvalidOperationException("1文字だけ入力してください。");
        var fontSize = Number(FontSizeBox);
        if (!double.IsFinite(fontSize) || fontSize < 1 || fontSize > 256 || string.IsNullOrWhiteSpace(FontBox.Text)) throw new InvalidOperationException("フォントと1～256pxの文字サイズを指定してください。");
        var cell = RequireCell(); cell.Text = TextBox.Text; cell.Font = FontBox.Text; cell.FontSize = fontSize; Changed();
    });
    private void ImageClick(object sender, RoutedEventArgs e) => Try(() =>
    {
        if (Surface.Selected is null) throw new InvalidOperationException("先にマスをクリックしてください。");
        var dialog = new OpenFileDialog { Filter = "画像|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff" };
        if (dialog.ShowDialog(this) != true) return;
        if (new FileInfo(dialog.FileName).Length > 20_000_000) throw new InvalidOperationException("画像は20MB以下にしてください。");
        var data = File.ReadAllBytes(dialog.FileName); _ = Document.DecodeImage(data);
        RequireCell().Image = data; Changed();
    });
    private void DeleteClick(object sender, RoutedEventArgs e)
    {
        var cell = SelectedCell(); if (cell is null) return;
        document.Cells.Remove(cell); ShowCell(); Changed();
    }
    private bool Save(bool saveAs)
    {
        var destination = path;
        if (saveAs || destination is null)
        {
            var dialog = new SaveFileDialog { Filter = "GridSketchプロジェクト|*.gridsketch", DefaultExt = ".gridsketch", FileName = path is null ? "無題.gridsketch" : Path.GetFileName(path) };
            if (dialog.ShowDialog(this) != true) return false;
            destination = dialog.FileName;
        }
        bool saved = false;
        Try(() => { document.Save(destination); path = destination; dirty = false; saved = true; UpdateTitle(); Status.Text = "プロジェクトを保存しました。"; RememberFile(destination); });
        return saved;
    }
    private bool CanDiscard()
    {
        if (!dirty) return true;
        var answer = MessageBox.Show(this, "変更を保存しますか？", "GridSketch", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer == MessageBoxResult.No || (answer == MessageBoxResult.Yes && Save(false));
    }
    private void SaveClick(object sender, RoutedEventArgs e) => Save(false);
    private void SaveAsClick(object sender, RoutedEventArgs e) => Save(true);
    private void ExitClick(object sender, RoutedEventArgs e) => Close();
    private void NewClick(object sender, RoutedEventArgs e)
    {
        if (!CanDiscard()) return; document = new(); path = null; dirty = false; ShowDocument();
    }
    private void OpenClick(object sender, RoutedEventArgs e) => Try(() =>
    {
        var dialog = new OpenFileDialog { Filter = "GridSketchプロジェクト|*.gridsketch" };
        if (dialog.ShowDialog(this) != true) return;
        OpenProject(dialog.FileName);
    });
    private void OpenProject(string filePath)
    {
        if (!CanDiscard()) return;
        var loaded = Document.Load(filePath);
        document = loaded; path = filePath; dirty = false; ShowDocument();
        Status.Text = "プロジェクトを開きました。";
        RememberFile(filePath);
    }
    private void RememberFile(string filePath)
    {
        if (!recentFiles.Remember(filePath)) Status.Text += " 最近使ったファイルの履歴を保存できませんでした。";
        RefreshRecentFilesMenu();
    }
    private void RefreshRecentFilesMenu()
    {
        RecentFilesMenu.Items.Clear();
        if (recentFiles.Entries.Count == 0)
        {
            RecentFilesMenu.Items.Add(new MenuItem { Header = "履歴はありません", IsEnabled = false });
            return;
        }
        foreach (var filePath in recentFiles.Entries)
        {
            var item = new MenuItem
            {
                // TextBlock keeps underscores in file names literal, rather than access keys.
                Header = new TextBlock { Text = filePath }, ToolTip = filePath
            };
            item.Click += (_, _) => Try(() =>
            {
                try { OpenProject(filePath); }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                {
                    bool persisted = recentFiles.Remove(filePath);
                    RefreshRecentFilesMenu();
                    Status.Text = persisted ? "見つからないファイルを履歴から削除しました。" : "履歴の変更を保存できませんでした。";
                    throw new FileNotFoundException($"ファイルが見つかりません。履歴から削除しました。\n{filePath}", ex);
                }
            });
            RecentFilesMenu.Items.Add(item);
        }
        RecentFilesMenu.Items.Add(new Separator());
        var clear = new MenuItem { Header = "履歴をクリア(_C)" };
        clear.Click += (_, _) =>
        {
            Status.Text = recentFiles.Clear() ? "履歴をクリアしました。" : "履歴の変更を保存できませんでした。";
            RefreshRecentFilesMenu();
        };
        RecentFilesMenu.Items.Add(clear);
    }
    private void ExportClick(object sender, RoutedEventArgs e) => Try(() =>
    {
        var dialog = new SaveFileDialog { Filter = "PNG画像|*.png", DefaultExt = ".png", FileName = Path.GetFileNameWithoutExtension(path ?? "無題") + ".png" };
        if (dialog.ShowDialog(this) != true) return;
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(Surface.Export(ExportScale.SelectedIndex + 1)));
        using var output = File.Create(dialog.FileName); encoder.Save(output); Status.Text = "PNGを書き出しました。";
    });
    private void ZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Surface is not null) Surface.LayoutTransform = new ScaleTransform(e.NewValue, e.NewValue);
    }
    private void WindowClosing(object? sender, CancelEventArgs e) => e.Cancel = !CanDiscard();
}
