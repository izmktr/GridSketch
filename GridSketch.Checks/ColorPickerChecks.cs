using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GridSketch;

internal static class ColorPickerChecks
{
    public static void Run(Window owner, string directory)
    {
        if (ColorPicker.FromHsv(0, 1, 1) != Colors.Red || ColorPicker.FromHsv(120, 1, 1) != Colors.Lime
            || ColorPicker.FromHsv(240, 1, 1) != Colors.Blue || ColorPicker.FromHsv(360, 1, 1) != Colors.Red
            || ColorPicker.FromHsv(90, 0, 1) != Colors.White || ColorPicker.FromHsv(90, 1, 0) != Colors.Black)
            throw new Exception("HSV conversion failed");
        var initial = Color.FromArgb(128, 42, 130, 210);
        if (!ColorPicker.TryColor(ColorPicker.Format(initial), out var roundTrip) || roundTrip != initial) throw new Exception("Alpha round trip failed");
        var historyPath = Path.Combine(directory, "recent-colors-" + Guid.NewGuid() + ".json");
        var history = new RecentColors(historyPath);
        for (byte i = 0; i < 10; i++) history.Remember(Color.FromRgb(i, 20, 30));
        if (history.Entries.Count != 8 || history.Entries[0].R != 9) throw new Exception("Color history limit or ordering failed");
        history.Remember(initial); history.Remember(initial);
        var loadedHistory = new RecentColors(historyPath);
        if (loadedHistory.Entries.Count != 8 || loadedHistory.Entries[0] != initial || loadedHistory.Entries.Count(c => c == initial) != 1)
            throw new Exception("Color history persistence, alpha or deduplication failed");
        var picker = new ColorPicker(initial, history) { Owner = owner }; picker.Show(); picker.UpdateLayout();
        IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i); if (child is T item) yield return item;
                foreach (var nested in Find<T>(child)) yield return nested;
            }
        }
        var boxes = Find<TextBox>(picker).ToList();
        var hex = boxes.Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "色コード");
        hex.Text = "#8044CC88";
        if (picker.SelectedColor != Color.FromArgb(128, 68, 204, 136)) throw new Exception("Hex did not update selected color");
        var red = boxes.Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "R");
        red.Text = "255";
        if (picker.SelectedColor.R != 255 || picker.SelectedColor.A != 128 || hex.Text != "#80FFCC88") throw new Exception("RGB/alpha synchronization failed");
        red.Text = "999";
        if (picker.SelectedColor.R != 255) throw new Exception("Invalid RGB accepted");
        hex.Text = "#2A82D2";
        var alpha = Find<Slider>(picker).Single(s => s.Maximum == 255); alpha.Value = 128;
        if (picker.SelectedColor.A != 128 || hex.Text != "#802A82D2") throw new Exception("Opacity synchronization failed");
        var themes = Find<ToggleButton>(picker).ToList();
        if (themes.Count != 6) throw new Exception("Missing palette themes");
        foreach (var theme in themes)
        {
            var previous = picker.SelectedColor;
            theme.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); picker.UpdateLayout();
            if (picker.SelectedColor != previous || themes.Count(t => t.IsChecked == true) != 1) throw new Exception("Theme selection changes color or is not exclusive");
            string prefix = theme.Content + " #";
            var swatches = Find<Button>(picker).Where(b => System.Windows.Automation.AutomationProperties.GetName(b).StartsWith(prefix)).ToList();
            if (swatches.Count != 8) throw new Exception("Palette does not contain eight colors");
            swatches[2].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            string code = System.Windows.Automation.AutomationProperties.GetName(swatches[2])[prefix.Length..];
            if (hex.Text != "#" + code || picker.SelectedColor.A != 255) throw new Exception("Preset failed to synchronize fields");
        }
        Find<Button>(picker).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "透明").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if (picker.SelectedColor.A != 0 || alpha.Value != 0) throw new Exception("Transparent preset failed");
        Find<Button>(picker).Single(b => System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("最近使った色 1 ")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if (picker.SelectedColor != initial || alpha.Value != initial.A) throw new Exception("History did not restore alpha");
        picker.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)picker.ActualWidth, (int)picker.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(picker);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var output = File.Create(Path.Combine(directory, "color-picker-preview.png"))) encoder.Save(output);
        picker.Close();
        if (!history.Entries.SequenceEqual(loadedHistory.Entries)) throw new Exception("Preview or cancellation changed history");
        var confirmed = Color.FromArgb(70, 80, 90, 100);
        var modal = new ColorPicker(confirmed, history) { Owner = owner };
        modal.Loaded += (_, _) => modal.Dispatcher.BeginInvoke(new Action(() =>
            Find<Button>(modal).Single(b => Equals(b.Content, "OK")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent))));
        if (modal.ShowDialog() != true || new RecentColors(historyPath).Entries[0] != confirmed) throw new Exception("OK did not persist selected color");
        var rejected = new ColorPicker(Colors.White, history) { Owner = owner };
        rejected.Loaded += (_, _) => rejected.Dispatcher.BeginInvoke(new Action(() =>
        {
            Find<TextBox>(rejected).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "色コード").Text = "invalid color";
            Find<Button>(rejected).Single(b => Equals(b.Content, "OK")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            if (!rejected.IsVisible) throw new Exception("Invalid color was confirmed");
            rejected.Close();
        }));
        rejected.ShowDialog();
        if (new RecentColors(historyPath).Entries[0] != confirmed) throw new Exception("Invalid color entered history");
        File.WriteAllText(historyPath, "invalid json");
        if (new RecentColors(historyPath).Entries.Count != 0) throw new Exception("Corrupt color history did not recover");
        var blocked = new RecentColors(Path.Combine(historyPath, "blocked.json"));
        if (blocked.Remember(initial) || blocked.Entries[0] != initial) throw new Exception("History write failure loses current-session color");
        File.Delete(historyPath);
        Console.WriteLine("PASS: Six eight-color palettes, transparency, history reuse, OK-only persistence, alpha, deduplication, capacity and recovery");
        Console.WriteLine("PASS: Color picker HSV, RGB, hex, alpha synchronization and rendered layout");
    }
}
