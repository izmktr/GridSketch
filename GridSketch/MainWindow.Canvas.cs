using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace GridSketch;

public partial class MainWindow
{
    private Action CanvasSettings(StackPanel panel, SceneObject canvas, Rect currentBounds)
    {
        void LabelHere(string text) => panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 2) });
        TextBox Field(string label, double value)
        {
            LabelHere(label); var box = new TextBox { Text = value.ToString("0.########", CultureInfo.CurrentCulture) }; panel.Children.Add(box); return box;
        }
        LabelHere($"現在の出力：{currentBounds.Width:0} × {currentBounds.Height:0} px\n用紙換算：{(canvas.AutoSize || canvas.CanvasUnit == "px" ? currentBounds.Width * 25.4 / canvas.Dpi : canvas.Width):0.##} × {(canvas.AutoSize || canvas.CanvasUnit == "px" ? currentBounds.Height * 25.4 / canvas.Dpi : canvas.Height):0.##} mm");
        LabelHere("サイズの決め方");
        var mode = new ComboBox { ItemsSource = new[] { "自動（内容に合わせる）", "固定（用紙・画像に合わせる）" }, SelectedIndex = canvas.AutoSize ? 0 : 1 }; panel.Children.Add(mode);
        var automaticPanel = new StackPanel(); panel.Children.Add(automaticPanel);
        var padding = new TextBox { Text = canvas.Padding.ToString(CultureInfo.CurrentCulture) };
        automaticPanel.Children.Add(new TextBlock { Text = "周囲の余白（px）" }); automaticPanel.Children.Add(padding);
        var fixedPanel = new StackPanel(); panel.Children.Add(fixedPanel);
        fixedPanel.Children.Add(new TextBlock { Text = "単位" });
        var unit = new ComboBox { ItemsSource = new[] { "px", "mm" }, SelectedItem = canvas.CanvasUnit }; fixedPanel.Children.Add(unit);
        fixedPanel.Children.Add(new TextBlock { Text = "用紙プリセット" });
        var preset = new ComboBox { ItemsSource = new[] { "カスタム", "A3", "A4", "A5", "A6", "B5（JIS）", "レター" }, SelectedIndex = 0 }; fixedPanel.Children.Add(preset);
        double factor = canvas.CanvasUnit == "mm" ? 25.4 / canvas.Dpi : 1;
        NumericPair(fixedPanel, "幅", canvas.AutoSize ? currentBounds.Width * factor : canvas.Width,
            "高さ", canvas.AutoSize ? currentBounds.Height * factor : canvas.Height, out var width, out var height, 1, 4000);
        var swap = new Button { Content = "縦・横を入れ替える" }; fixedPanel.Children.Add(swap);
        swap.Click += (_, _) => (width.Text, height.Text) = (height.Text, width.Text);
        fixedPanel.Children.Add(new TextBlock { Text = "出力範囲の左上（編集座標 / px）" });
        NumericPair(fixedPanel, "X", canvas.AutoSize ? currentBounds.X : canvas.X, "Y", canvas.AutoSize ? currentBounds.Y : canvas.Y, out var x, out var y);
        var dpi = Field("解像度（dpi）", canvas.Dpi);
        LabelHere("px指定・自動サイズではdpiは用紙サイズの換算に使います。mm指定ではdpiに応じてPNGの画素数が変わります。オブジェクトは移動・拡大縮小しません。");
        LabelHere("背景色"); panel.Children.Add(ColorEditor(canvas.Background, out var background));
        var estimate = new TextBlock { TextWrapping = TextWrapping.Wrap }; panel.Children.Add(estimate);
        bool changingPreset = false; string previousUnit = canvas.CanvasUnit;
        bool Parse(TextBox box, out double value) => double.TryParse(box.Text, out value) && double.IsFinite(value);
        void Estimate()
        {
            automaticPanel.Visibility = mode.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            fixedPanel.Visibility = mode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            if (mode.SelectedIndex == 0) { estimate.Text = "内容と余白に合わせて出力範囲を自動更新します。"; return; }
            if (Parse(width, out var w) && Parse(height, out var h) && Parse(dpi, out var d) && w > 0 && h > 0 && d > 0)
            {
                bool mm = unit.SelectedIndex == 1;
                estimate.Text = $"PNG：{(mm ? Math.Max(1, Math.Round(w * d / 25.4)) : Math.Ceiling(w)):0} × {(mm ? Math.Max(1, Math.Round(h * d / 25.4)) : Math.Ceiling(h)):0} px\n用紙：{(mm ? w : w * 25.4 / d):0.##} × {(mm ? h : h * 25.4 / d):0.##} mm\n枠外の内容は保持し、出力時に切り取ります。";
            }
            else estimate.Text = "幅・高さ・解像度を確認してください。";
        }
        mode.SelectionChanged += (_, _) =>
        {
            if (mode.SelectedIndex == 1 && Parse(padding, out var p) && p >= 0 && p <= 1000 && Parse(dpi, out var d) && d > 0)
            {
                var preview = SceneDocument.Restore(new SceneDocument { Canvas = canvas }.Snapshot());
                preview.Canvas.AutoSize = true; preview.Canvas.Padding = p;
                var fitted = new SceneRenderer(preview).OutputBounds;
                double f = unit.SelectedIndex == 1 ? 25.4 / d : 1;
                width.Text = (fitted.Width * f).ToString("0.########", CultureInfo.CurrentCulture);
                height.Text = (fitted.Height * f).ToString("0.########", CultureInfo.CurrentCulture);
                x.Text = fitted.X.ToString(CultureInfo.CurrentCulture); y.Text = fitted.Y.ToString(CultureInfo.CurrentCulture);
            }
            Estimate();
        };
        unit.SelectionChanged += (_, _) =>
        {
            string next = (string)unit.SelectedItem;
            if (!changingPreset && next != previousUnit && Parse(width, out var w) && Parse(height, out var h) && Parse(dpi, out var d) && d > 0)
            {
                double f = next == "mm" ? 25.4 / d : d / 25.4;
                width.Text = (w * f).ToString("0.########", CultureInfo.CurrentCulture); height.Text = (h * f).ToString("0.########", CultureInfo.CurrentCulture);
            }
            previousUnit = next; Estimate();
        };
        preset.SelectionChanged += (_, _) =>
        {
            if (preset.SelectedIndex <= 0) return;
            (double w, double h) = preset.SelectedIndex switch { 1 => (297, 420), 2 => (210, 297), 3 => (148, 210), 4 => (105, 148), 5 => (182, 257), _ => (215.9, 279.4) };
            changingPreset = true; unit.SelectedItem = "mm"; width.Text = w.ToString(CultureInfo.CurrentCulture); height.Text = h.ToString(CultureInfo.CurrentCulture); changingPreset = false; Estimate();
        };
        foreach (var box in new[] { width, height }) box.TextChanged += (_, _) => { if (!changingPreset) preset.SelectedIndex = 0; Estimate(); };
        dpi.TextChanged += (_, _) => Estimate(); Estimate();
        return () =>
        {
            canvas.AutoSize = mode.SelectedIndex == 0; canvas.Dpi = double.Parse(dpi.Text); canvas.Background = background.Text;
            if (canvas.AutoSize) canvas.Padding = double.Parse(padding.Text);
            else
            {
                canvas.CanvasUnit = (string)unit.SelectedItem; canvas.Width = double.Parse(width.Text); canvas.Height = double.Parse(height.Text);
                canvas.X = double.Parse(x.Text); canvas.Y = double.Parse(y.Text);
            }
        };
    }
}
