using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GridSketch;

internal static class BatchExportChecks
{
    public static void Run(MainWindow window, string directory)
    {
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        var folder = Path.Combine(directory, "batch-export-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        var scene = new SceneDocument(); scene.Canvas.AutoSize = false; scene.Canvas.Width = 80; scene.Canvas.Height = 60;
        scene.Canvas.Children.Add(new SceneObject { Kind = ObjectKind.Text, Text = "出力", FontSize = 20 });
        var snapshot = scene.Snapshot();
        File.WriteAllText(Path.Combine(folder, "無題.png"), "existing");
        var results = BatchExporter.Export([new("無題", scene), new("無題", scene), new("CON", scene), new("a/b", scene)], folder, "PNG");
        Check(results.All(r => r.Error is null) && results.Select(r => r.Path).Distinct().Count() == 4, "Batch PNG export failed");
        Check(File.ReadAllText(Path.Combine(folder, "無題.png")) == "existing" && File.Exists(Path.Combine(folder, "無題 (2).png"))
            && File.Exists(Path.Combine(folder, "無題 (3).png")), "Batch export must never overwrite duplicate names");
        var image = BitmapFrame.Create(new Uri(results[0].Path!), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Check(image.PixelWidth == 80 && image.PixelHeight == 60, "Batch export must use each canvas size");
        results = BatchExporter.Export([new("文書", scene)], folder, "PDF");
        Check(results[0].Error is null && System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(results[0].Path!), 0, 5) == "%PDF-", "Batch PDF export failed");
        var invalid = SceneDocument.Restore(snapshot); invalid.Canvas.Width = -1;
        results = BatchExporter.Export([new("失敗", invalid), new("成功", scene)], folder, "PNG");
        Check(results[0].Error is not null && results[1].Error is null && !Directory.EnumerateFiles(folder, "*.tmp").Any(),
            "Batch export must report failures and continue without temporary files");
        Check(scene.Snapshot() == snapshot, "Export must not modify source documents");
        var dialog = new BatchExportDialog([new("選択", scene), new("対象外", scene)], folder) { Owner = window };
        dialog.Show(); dialog.UpdateLayout();
        var export = PropertyChecks.Find<Button>(dialog, "選択したタブを出力");
        var first = PropertyChecks.Find<CheckBox>(dialog, "出力対象：1. 選択");
        var second = PropertyChecks.Find<CheckBox>(dialog, "出力対象：2. 対象外");
        first.IsChecked = second.IsChecked = false;
        Check(!export.IsEnabled, "Empty batch selection must disable export");
        first.IsChecked = true;
        PropertyChecks.Find<ComboBox>(dialog, "一括出力形式").SelectedItem = "PDF";
        export.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(dialog.Results.Count == 1 && dialog.Results[0].Error is null && File.Exists(Path.Combine(folder, "選択.pdf"))
            && !File.Exists(Path.Combine(folder, "対象外.pdf")), "Batch dialog must export only selected tabs in the chosen format");
        dialog.UpdateLayout();
        var png = new RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, PixelFormats.Pbgra32); png.Render(dialog);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(png));
        using (var output = File.Create(Path.Combine(directory, "batch-export-dialog.png"))) encoder.Save(output);
        dialog.Close();
        int count = Directory.EnumerateFiles(folder).Count();
        var cancelled = new BatchExportDialog([new("キャンセル", scene)], folder) { Owner = window };
        cancelled.Show(); cancelled.Close();
        Check(Directory.EnumerateFiles(folder).Count() == count, "Closing the dialog must not export");
        scene.VisibilityPresets = [new() { Name = "問題", HiddenIds = [scene.Canvas.Children[0].Id] }, new() { Name = "答え" }];
        var secondScene = SceneDocument.Restore(scene.Snapshot());
        secondScene.VisibilityPresets = [new() { Name = "問題" }, new() { Name = "別紙" }];
        var sources = new[] { new BatchExportItem("A", scene), new BatchExportItem("B", secondScene) };
        var plan = BatchExporter.Plan(sources, ["問題", "答え"]);
        Check(plan.Jobs.Count == 3 && plan.Excluded.Count == 1 && plan.Excluded[0].Contains("B：答え"), "Missing presets must exclude only their own combination");
        snapshot = scene.Snapshot();
        var presetResults = BatchExporter.Export(plan.Jobs, folder, "PNG");
        Check(presetResults.All(r => r.Error is null) && File.Exists(Path.Combine(folder, "A_問題.png"))
            && File.Exists(Path.Combine(folder, "A_答え.png")) && !File.Exists(Path.Combine(folder, "B_答え.png")), "Preset export file naming or exclusion failed");
        Check(!File.ReadAllBytes(Path.Combine(folder, "A_問題.png")).SequenceEqual(File.ReadAllBytes(Path.Combine(folder, "A_答え.png")))
            && scene.Snapshot() == snapshot, "Preset export must apply visibility to a copy only");
        var presetsDialog = new BatchExportDialog(sources, folder) { Owner = window }; presetsDialog.Show(); presetsDialog.UpdateLayout();
        PropertyChecks.Find<ComboBox>(presetsDialog, "一括出力の表示方法").SelectedIndex = 1;
        var presetPopup = PropertyChecks.Find<Popup>(presetsDialog, "出力プリセット候補");
        PropertyChecks.Find<CheckBox>(presetPopup.Child, "出力プリセット：問題").IsChecked = true;
        PropertyChecks.Find<CheckBox>(presetPopup.Child, "出力プリセット：答え").IsChecked = true;
        Check(PropertyChecks.Find<TextBlock>(presetsDialog, "一括出力予定").Text == "3件出力・1件除外", "Preset counts must update on selection");
        PropertyChecks.Find<CheckBox>(presetsDialog, "出力対象：2. B").IsChecked = false;
        Check(PropertyChecks.Find<TextBlock>(presetsDialog, "一括出力予定").Text == "2件出力・0件除外"
            && ((string)PropertyChecks.Find<CheckBox>(presetPopup.Child, "出力プリセット：問題").Content).Contains("1/1"), "Unchecked tabs must not contribute to candidates or counts");
        var presetRows = (StackPanel)((ScrollViewer)((Border)presetPopup.Child).Child).Content;
        Check(presetRows.Children.OfType<CheckBox>().Count() == 2, "A preset found only in unchecked tabs must disappear");
        PropertyChecks.Find<CheckBox>(presetsDialog, "出力対象：2. B").IsChecked = true;
        presetsDialog.UpdateLayout();
        png = new RenderTargetBitmap((int)presetsDialog.ActualWidth, (int)presetsDialog.ActualHeight, 96, 96, PixelFormats.Pbgra32); png.Render(presetsDialog);
        encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(png));
        using (var output = File.Create(Path.Combine(directory, "batch-presets-dialog.png"))) encoder.Save(output);
        presetsDialog.Close();
        Console.WriteLine("PASS: Batch PNG/PDF, tab selection, duplicates, original preservation, partial failure and cancellation");
    }
}
