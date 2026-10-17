using System.IO;
using System.Windows;
using System.Windows.Controls;
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
        var picker = new ColorPicker(initial) { Owner = owner }; picker.Show(); picker.UpdateLayout();
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
        picker.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)picker.ActualWidth, (int)picker.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(picker);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var output = File.Create(Path.Combine(directory, "color-picker-preview.png"))) encoder.Save(output);
        picker.Close();
        Console.WriteLine("PASS: Color picker HSV, RGB, hex, alpha synchronization and rendered layout");
    }
}
