using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GridSketch;

internal static class PrintExportSettingsChecks
{
    public static void Run(MainWindow window, string directory)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var doc = new SceneDocument(); doc.Canvas.AutoSize = false;
        typeof(MainWindow).GetField("document", flags)!.SetValue(window, doc);
        typeof(MainWindow).GetField("selectedId", flags)!.SetValue(window, doc.Canvas.Id);
        typeof(MainWindow).GetMethod("RefreshAll", flags)!.Invoke(window, null);
        Exception? failure = null;
        window.Dispatcher.BeginInvoke(new Action(() =>
        {
            var dialog = window.OwnedWindows.Cast<Window>().Single(w => w.Title == "エクスポート設定");
            try
            {
                PropertyChecks.Find<ComboBox>(dialog, "PDFプリセット").SelectedItem = "印刷会社用";
                var cmyk = PropertyChecks.Find<CheckBox>(dialog, "PDF CMYK変換");
                if (cmyk.IsChecked != true) throw new Exception("Company preset did not enable CMYK");
                var profile = PropertyChecks.Find<ComboBox>(dialog, "PDF ICCプロファイル");
                if (profile.SelectedIndex < 0) cmyk.IsChecked = false;
                PropertyChecks.Find<TextBox>(dialog, "PDF塗り足し").Text = "5";
                PropertyChecks.Find<CheckBox>(dialog, "PDFトンボ").IsChecked = true;
                dialog.UpdateLayout();
                var image = new RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(dialog);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using (var output = File.Create(Path.Combine(directory, "print-export-settings.png"))) encoder.Save(output);
                PropertyChecks.Find<Button>(dialog, "エクスポート設定を保存").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }
            catch (Exception ex) { failure = ex; dialog.Close(); }
        }));
        typeof(MainWindow).GetMethod("ExportSettingsClick", flags)!.Invoke(window, [window, new RoutedEventArgs()]);
        if (failure is not null) throw failure;
        if (!doc.PdfExport.PrintCompany || doc.PdfExport.BleedMm != 5 || !doc.PdfExport.CropMarks) throw new Exception("Print settings dialog did not save edits");
        typeof(MainWindow).GetMethod("Undo", flags)!.Invoke(window, null);
        var restored = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        if (restored.PdfExport.PrintCompany) throw new Exception("Print settings undo failed");
        Console.WriteLine("PASS: Print company settings dialog, preset, edited values, profile selection and undo");
    }
}
