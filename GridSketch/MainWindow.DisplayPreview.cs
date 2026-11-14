using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;

namespace GridSketch;

public partial class MainWindow
{
    private Window? displayPreviewWindow;
    private CheckBox? cmykPreview;
    private TextBlock? previewProfile, previewMessage;
    private ColorVisionPreview visionPreview;
    private PdfColorConverter? previewConverter;
    private byte[]? previewProfileBytes;
    private void DisplayPreviewClick(object sender, RoutedEventArgs e)
    {
        if (displayPreviewWindow is not null) { displayPreviewWindow.Activate(); return; }
        visionPreview = ColorVisionPreview.None;
        var dialog = new Window { Title = "表示確認", Owner = this, Width = 390, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        displayPreviewWindow = dialog;
        var panel = new StackPanel { Margin = new Thickness(18) }; dialog.Content = panel;
        panel.Children.Add(new TextBlock { Text = "印刷色", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        cmykPreview = new CheckBox { Content = "CMYKプレビュー", Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetName(cmykPreview, "CMYKプレビュー"); panel.Children.Add(cmykPreview);
        previewProfile = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray }; panel.Children.Add(previewProfile);
        var export = new Button { Content = "エクスポート設定…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 16) };
        export.Click += (_, _) => ExportSettingsClick(this, new RoutedEventArgs()); panel.Children.Add(export);
        panel.Children.Add(new TextBlock { Text = "色覚シミュレーション", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        foreach (var (name, mode) in new[] { ("なし", ColorVisionPreview.None), ("1型（P型）", ColorVisionPreview.Protan), ("2型（D型）", ColorVisionPreview.Deutan), ("3型（T型）", ColorVisionPreview.Tritan) })
        {
            var radio = new RadioButton { Content = name, GroupName = "ColorVision", IsChecked = mode == ColorVisionPreview.None, Margin = new Thickness(0, 4, 0, 4) };
            AutomationProperties.SetName(radio, "色覚：" + name); panel.Children.Add(radio);
            radio.Checked += (_, _) => { visionPreview = mode; UpdateDisplayPreview(); };
        }
        previewMessage = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick, Margin = new Thickness(0, 8, 0, 0) }; panel.Children.Add(previewMessage);
        panel.Children.Add(new TextBlock { Text = "画面だけに適用します。閉じると通常表示へ戻ります。両方を有効にするとCMYK→色覚の順で確認します。\n\nCMYKは色変換の目安です。用紙色・透明効果を含む印刷結果の完全再現ではありません。色覚は二色覚相当のシミュレーションで、個人の見え方を再現・診断するものではありません。", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 14, 0, 0) });
        cmykPreview.Checked += (_, _) => UpdateDisplayPreview(); cmykPreview.Unchecked += (_, _) => UpdateDisplayPreview();
        Surface.DisplayPreviewError = message => { if (previewMessage is not null) previewMessage.Text = message; };
        dialog.Closed += (_, _) =>
        {
            displayPreviewWindow = null; cmykPreview = null; previewProfile = previewMessage = null;
            previewConverter = null; previewProfileBytes = null; visionPreview = ColorVisionPreview.None;
            Surface.DisplayPreviewError = null; Surface.SetDisplayPreview(null, ColorVisionPreview.None);
        };
        UpdateDisplayPreview(); dialog.Show();
    }
    private void UpdateDisplayPreview()
    {
        if (displayPreviewWindow is null || cmykPreview is null) return;
        var settings = document.PdfExport;
        previewProfile!.Text = settings.ProfileData is null ? "ICC未設定：エクスポート設定で指定してください。" : "ICC：" + settings.ProfileName;
        cmykPreview.IsEnabled = settings.ProfileData is not null;
        if (!cmykPreview.IsEnabled) cmykPreview.IsChecked = false;
        PdfColorConverter? converter = null;
        previewMessage!.Text = "";
        if (cmykPreview.IsChecked == true)
        {
            try
            {
                if (previewConverter is null || previewProfileBytes is null || !previewProfileBytes.SequenceEqual(settings.ProfileData!))
                { previewConverter = new PdfColorConverter(settings.ProfileData!); previewProfileBytes = settings.ProfileData!.ToArray(); }
                converter = previewConverter;
            }
            catch (Exception ex) { cmykPreview.IsChecked = false; previewMessage.Text = "ICCを使用できません：" + ex.Message; }
        }
        Surface.SetDisplayPreview(converter, visionPreview);
    }
}
