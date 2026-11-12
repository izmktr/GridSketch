using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;

namespace GridSketch;

public partial class MainWindow
{
    private void BatchExportClick(object sender, RoutedEventArgs e) => Try(() =>
    {
        CommitPropertyEdit(); FinishDrag();
        if (!gridComposing && GridInput.Text.Length > 0 && CanTypeInGrid) CommitGridInput(GridInput.Text);
        CaptureTab();
        var items = editorTabs.Select(tab => new BatchExportItem(tab.Owner is not null ? "部品_" + tab.Name
            : tab.Path is null ? "無題" : System.IO.Path.GetFileNameWithoutExtension(tab.Path), tab.Document)).ToList();
        var dialog = new BatchExportDialog(items, path is null ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            : System.IO.Path.GetDirectoryName(path)!) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Results.Count > 0) Status.Text = $"一括エクスポート：{dialog.Results.Count(r => r.Error is null)}件を出力しました。";
    });

    private void ExportPdfClick(object sender, RoutedEventArgs e) => Try(() =>
    {
        var dialog = new SaveFileDialog { Filter = "PDF文書|*.pdf", DefaultExt = ".pdf", FileName = System.IO.Path.GetFileNameWithoutExtension(path ?? "無題") + ".pdf" };
        if (dialog.ShowDialog(this) != true) return;
        PdfExporter.Save(document, dialog.FileName); Status.Text = "PDFを書き出しました。";
    });
    private void ExportSettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Window { Title = "エクスポート設定", Owner = this, Width = 460, SizeToContent = SizeToContent.Height, MaxHeight = 760, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(20) }; dialog.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        void LabelHere(string text) => panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 4) });
        LabelHere("PDF出力設定");
        var renderer = new SceneRenderer(document); var size = PdfExporter.PageSize(renderer);
        LabelHere($"1ページ：{size.Width * 25.4 / 72:0.##} × {size.Height * 25.4 / 72:0.##} mm\nPNG：{renderer.OutputBounds.Width:0} × {renderer.OutputBounds.Height:0} px\n用紙サイズと背景はキャンバスの設定を使用します。");
        LabelHere("プリセット");
        var preset = new ComboBox { ItemsSource = new[] { "印刷用", "軽量", "品質優先", "カスタム" }, SelectedItem = document.PdfExport.Preset }; panel.Children.Add(preset);
        LabelHere("画像の解像度上限（dpi / 0は元の解像度を維持）");
        var dpi = new TextBox { Text = document.PdfExport.MaxImageDpi.ToString() }; panel.Children.Add(dpi);
        LabelHere("画像の圧縮方式");
        var compression = new ComboBox { ItemsSource = new[] { "JPEG（小さくする・画質が変化）", "可逆圧縮（画質を維持）" }, SelectedIndex = document.PdfExport.JpegCompression ? 0 : 1 }; panel.Children.Add(compression);
        LabelHere("JPEG品質（1～100）"); var quality = new TextBox { Text = document.PdfExport.JpegQuality.ToString(), IsEnabled = document.PdfExport.JpegCompression }; panel.Children.Add(quality);
        LabelHere("印刷用：画像300dpi・JPEG品質90（家庭・オフィス向け）\n軽量：画像150dpi・JPEG品質75\n品質優先：元の解像度・可逆圧縮\n画像の拡大補間は行いません。透明部分は全設定で保持します。");
        LabelHere("文字は輪郭に変換し、罫線・図形・矢印とともにベクターで出力します。PDF内の文字検索・コピーには対応しません。PNG固有の設定はありません。");
        bool updating = false;
        void Custom()
        {
            quality.IsEnabled = compression.SelectedIndex == 0;
            if (!updating) preset.SelectedItem = "カスタム";
        }
        dpi.TextChanged += (_, _) => Custom(); quality.TextChanged += (_, _) => Custom(); compression.SelectionChanged += (_, _) => Custom();
        preset.SelectionChanged += (_, _) =>
        {
            if (preset.SelectedItem is not string name || name == "カスタム") return;
            updating = true; var values = PdfSettings.ForPreset(name); dpi.Text = values.MaxImageDpi.ToString(); quality.Text = values.JpegQuality.ToString(); compression.SelectedIndex = values.JpegCompression ? 0 : 1; updating = false;
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(buttons);
        var cancel = new Button { Content = "キャンセル", IsCancel = true }; buttons.Children.Add(cancel);
        var ok = new Button { Content = "OK", IsDefault = true }; buttons.Children.Add(ok);
        PdfSettings? result = null;
        ok.Click += (_, _) => Try(() =>
        {
            var value = new PdfSettings { MaxImageDpi = int.Parse(dpi.Text), JpegCompression = compression.SelectedIndex == 0, JpegQuality = int.Parse(quality.Text) };
            value.Validate(); result = value; dialog.DialogResult = true;
        });
        if (dialog.ShowDialog() == true && result is not null) Change(() => document.PdfExport = result);
    }
}
