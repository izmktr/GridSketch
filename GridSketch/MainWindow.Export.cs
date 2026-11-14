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
        CommitPropertyEdit(); FinishDrag();
        if (!gridComposing && GridInput.Text.Length > 0 && CanTypeInGrid) CommitGridInput(GridInput.Text);
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
        LabelHere($"仕上がり：{size.Width * 25.4 / 72:0.##} × {size.Height * 25.4 / 72:0.##} mm\nPNG：{renderer.OutputBounds.Width:0} × {renderer.OutputBounds.Height:0} px\n用紙サイズと背景はキャンバスの設定を使用します。");
        LabelHere("プリセット");
        var preset = new ComboBox { ItemsSource = new[] { "印刷用", "印刷会社用", "軽量", "品質優先", "カスタム" }, SelectedItem = document.PdfExport.Preset }; panel.Children.Add(preset);
        bool company = document.PdfExport.PrintCompany;
        byte[]? profileData = document.PdfExport.ProfileData;
        string profileName = document.PdfExport.ProfileName;
        var printPanel = new StackPanel(); panel.Children.Add(printPanel);
        void PrintLabel(string text) => printPanel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 4) });
        PrintLabel("塗り足し（上下左右 / mm）");
        var bleed = new TextBox { Text = document.PdfExport.BleedMm.ToString() }; printPanel.Children.Add(bleed);
        var marks = new CheckBox { Content = "トンボを付ける", IsChecked = document.PdfExport.CropMarks, Margin = new Thickness(0, 6, 0, 4) }; printPanel.Children.Add(marks);
        var cmyk = new CheckBox { Content = "CMYKへ変換する", IsChecked = document.PdfExport.ConvertToCmyk, Margin = new Thickness(0, 6, 0, 4) }; printPanel.Children.Add(cmyk);
        var colorPanel = new StackPanel(); printPanel.Children.Add(colorPanel);
        colorPanel.Children.Add(new TextBlock { Text = "変換先ICCプロファイル", Margin = new Thickness(0, 6, 0, 4) });
        var profiles = new List<(string Name, byte[] Bytes)>();
        var profileFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "spool", "drivers", "color");
        if (System.IO.Directory.Exists(profileFolder))
            foreach (var file in System.IO.Directory.EnumerateFiles(profileFolder).Where(f => System.IO.Path.GetExtension(f).ToLowerInvariant() is ".icc" or ".icm"))
                try { var bytes = System.IO.File.ReadAllBytes(file); PdfSettings.ValidateProfile(bytes); profiles.Add((System.IO.Path.GetFileNameWithoutExtension(file), bytes)); }
                catch (Exception ex) when (ex is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException) { }
        if (profileData is not null) profiles.Insert(0, (profileName, profileData));
        var profile = new ComboBox { ItemsSource = profiles.Select(p => p.Name).ToList(), SelectedIndex = profileData is null ? -1 : 0 }; colorPanel.Children.Add(profile);
        var browse = new Button { Content = "ICCファイルを選択…", HorizontalAlignment = HorizontalAlignment.Left }; colorPanel.Children.Add(browse);
        var black = new CheckBox { Content = "文字・線・図形の純黒をK100%にする", IsChecked = document.PdfExport.PreserveBlack, Margin = new Thickness(0, 6, 0, 4) }; colorPanel.Children.Add(black);
        PrintLabel("仕上がりサイズは固定サイズのキャンバスから取得します。塗り足しまで背景・絵柄を配置してください。CMYK画像は可逆圧縮で出力します。文字はアウトライン化します。PDF/X準拠ではありません。");
        PrintLabel("ICCは入稿先の指定に合わせて選択してください。自動選択されるJapanColor2001Coatedはプリントオンの推奨を意味しません。選んだICCはプロジェクトとPDFに保存します。");
        LabelHere("画像の解像度上限（dpi / 0は元の解像度を維持）");
        var dpi = new TextBox { Text = document.PdfExport.MaxImageDpi.ToString() }; panel.Children.Add(dpi);
        LabelHere("画像の圧縮方式");
        var compression = new ComboBox { ItemsSource = new[] { "JPEG（小さくする・画質が変化）", "可逆圧縮（画質を維持）" }, SelectedIndex = document.PdfExport.JpegCompression ? 0 : 1 }; panel.Children.Add(compression);
        LabelHere("JPEG品質（1～100）"); var quality = new TextBox { Text = document.PdfExport.JpegQuality.ToString(), IsEnabled = document.PdfExport.JpegCompression }; panel.Children.Add(quality);
        LabelHere("印刷用：画像300dpi・JPEG品質90（家庭・オフィス向け）\n軽量：画像150dpi・JPEG品質75\n品質優先：元の解像度・可逆圧縮\n画像の拡大補間は行いません。透明効果は保持します。印刷会社用では用紙を白として出力します。");
        LabelHere("文字は輪郭に変換し、罫線・図形・矢印とともにベクターで出力します。PDF内の文字検索・コピーには対応しません。PNG固有の設定はありません。");
        bool updating = false;
        void PrintControls()
        {
            printPanel.Visibility = company ? Visibility.Visible : Visibility.Collapsed;
            colorPanel.Visibility = cmyk.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            if (company && cmyk.IsChecked == true && compression.SelectedIndex != 1) compression.SelectedIndex = 1;
            compression.IsEnabled = !(company && cmyk.IsChecked == true);
            quality.IsEnabled = compression.IsEnabled && compression.SelectedIndex == 0;
        }
        profile.SelectionChanged += (_, _) => { if (profile.SelectedIndex >= 0) { var selected = profiles[profile.SelectedIndex]; profileData = selected.Bytes; profileName = selected.Name; } };
        browse.Click += (_, _) => Try(() =>
        {
            var picker = new OpenFileDialog { Filter = "ICCプロファイル|*.icc;*.icm" };
            if (picker.ShowDialog(dialog) != true) return;
            var bytes = System.IO.File.ReadAllBytes(picker.FileName); PdfSettings.ValidateProfile(bytes);
            profiles.Add((System.IO.Path.GetFileNameWithoutExtension(picker.FileName), bytes));
            profile.ItemsSource = profiles.Select(p => p.Name).ToList(); profile.SelectedIndex = profiles.Count - 1;
        });
        cmyk.Checked += (_, _) => PrintControls(); cmyk.Unchecked += (_, _) => PrintControls();
        PrintControls();
        void Custom()
        {
            PrintControls();
            if (!updating && !company) preset.SelectedItem = "カスタム";
        }
        dpi.TextChanged += (_, _) => Custom(); quality.TextChanged += (_, _) => Custom(); compression.SelectionChanged += (_, _) => Custom();
        preset.SelectionChanged += (_, _) =>
        {
            if (preset.SelectedItem is not string name || name == "カスタム") return;
            updating = true; var values = PdfSettings.ForPreset(name); dpi.Text = values.MaxImageDpi.ToString(); quality.Text = values.JpegQuality.ToString(); compression.SelectedIndex = values.JpegCompression ? 0 : 1;
            company = values.PrintCompany; bleed.Text = values.BleedMm.ToString(); marks.IsChecked = values.CropMarks; cmyk.IsChecked = values.ConvertToCmyk; black.IsChecked = values.PreserveBlack;
            if (company && profileData is null && values.ProfileData is not null)
            {
                profiles.Add((values.ProfileName, values.ProfileData)); profile.ItemsSource = profiles.Select(p => p.Name).ToList(); profile.SelectedIndex = profiles.Count - 1;
            }
            PrintControls(); updating = false;
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(buttons);
        var cancel = new Button { Content = "キャンセル", IsCancel = true }; buttons.Children.Add(cancel);
        var ok = new Button { Content = "OK", IsDefault = true }; buttons.Children.Add(ok);
        System.Windows.Automation.AutomationProperties.SetName(preset, "PDFプリセット");
        System.Windows.Automation.AutomationProperties.SetName(bleed, "PDF塗り足し");
        System.Windows.Automation.AutomationProperties.SetName(marks, "PDFトンボ");
        System.Windows.Automation.AutomationProperties.SetName(cmyk, "PDF CMYK変換");
        System.Windows.Automation.AutomationProperties.SetName(profile, "PDF ICCプロファイル");
        System.Windows.Automation.AutomationProperties.SetName(ok, "エクスポート設定を保存");
        PdfSettings? result = null;
        ok.Click += (_, _) => Try(() =>
        {
            var value = new PdfSettings { MaxImageDpi = int.Parse(dpi.Text), JpegCompression = compression.SelectedIndex == 0, JpegQuality = int.Parse(quality.Text),
                PrintCompany = company, BleedMm = company ? double.Parse(bleed.Text) : 0, CropMarks = company && marks.IsChecked == true,
                ConvertToCmyk = company && cmyk.IsChecked == true, PreserveBlack = black.IsChecked == true, ProfileName = profileName, ProfileData = profileData };
            if (value.PrintCompany && value.ConvertToCmyk && value.ProfileData is null) throw new InvalidOperationException("CMYKのICCプロファイルを選択してください。");
            value.Validate(); result = value; dialog.DialogResult = true;
        });
        if (dialog.ShowDialog() == true && result is not null) Change(() => document.PdfExport = result);
    }
}
