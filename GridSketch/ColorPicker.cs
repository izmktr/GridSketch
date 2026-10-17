using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GridSketch;

public sealed class ColorPicker : Window
{
    private readonly SaturationValueBox field = new();
    private readonly Slider hue = new() { Minimum = 0, Maximum = 360, Width = 32, Height = 260, Orientation = Orientation.Vertical, IsDirectionReversed = true };
    private readonly Slider alpha = new() { Minimum = 0, Maximum = 255, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 200 };
    private readonly TextBox hex = new();
    private readonly TextBox[] channels = [new(), new(), new(), new()];
    private readonly Border preview = Swatch();
    private readonly TextBlock error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, MinHeight = 36 };
    private bool updating;
    public Color SelectedColor { get; private set; }

    public ColorPicker(Color initial)
    {
        Title = "色を選択"; SizeToContent = SizeToContent.WidthAndHeight; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new StackPanel { Margin = new Thickness(20), Width = 550 }; Content = root;
        root.Children.Add(new TextBlock { Text = "色を選択", FontSize = 20, Margin = new Thickness(0, 0, 0, 12) });
        var body = new StackPanel { Orientation = Orientation.Horizontal }; root.Children.Add(body);
        body.Children.Add(field); body.Children.Add(hue);
        var side = new StackPanel { Width = 235, Margin = new Thickness(16, 0, 0, 0) }; body.Children.Add(side);
        side.Children.Add(new TextBlock { Text = "変更前                     変更後" });
        var samples = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 12) };
        var old = Swatch(); old.Child = new Border { Background = new SolidColorBrush(initial) }; samples.Children.Add(old); samples.Children.Add(preview); side.Children.Add(samples);
        side.Children.Add(new TextBlock { Text = "RGB / 不透明度（0～255）" });
        var values = new StackPanel { Orientation = Orientation.Horizontal };
        string[] names = ["R", "G", "B", "A"];
        for (int i = 0; i < channels.Length; i++)
        {
            var column = new StackPanel { Width = 54 }; column.Children.Add(new TextBlock { Text = names[i] });
            channels[i].Margin = new Thickness(0, 3, 4, 8); column.Children.Add(channels[i]); values.Children.Add(column);
            channels[i].TextChanged += (_, _) => ReadChannels();
            System.Windows.Automation.AutomationProperties.SetName(channels[i], names[i]);
        }
        side.Children.Add(values);
        side.Children.Add(new TextBlock { Text = "色コード #RRGGBB / #AARRGGBB" }); side.Children.Add(hex);
        System.Windows.Automation.AutomationProperties.SetName(hex, "色コード");
        hex.TextChanged += (_, _) =>
        {
            if (updating) return;
            if (TryColor(hex.Text, out var color)) { SetColor(color, false, true); error.Text = ""; }
            else error.Text = "色コードを確認してください。";
        };
        side.Children.Add(new TextBlock { Text = "不透明度（左：透明 / 右：不透明）" }); side.Children.Add(alpha);
        side.Children.Add(error);
        var spectrum = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        for (int i = 0; i <= 6; i++) spectrum.GradientStops.Add(new GradientStop(FromHsv(i * 60, 1, 1), i / 6.0));
        hue.Background = spectrum;
        hue.ValueChanged += (_, _) => { if (!updating) { field.Hue = hue.Value; FromField(); } };
        field.Changed += FromField;
        alpha.ValueChanged += (_, _) => { if (!updating) SetColor(Color.FromArgb((byte)alpha.Value, SelectedColor.R, SelectedColor.G, SelectedColor.B), true); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = "キャンセル", IsCancel = true, MinWidth = 100 };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 90 };
        ok.Click += (_, _) => { if (error.Text.Length == 0) DialogResult = true; };
        buttons.Children.Add(cancel); buttons.Children.Add(ok); root.Children.Add(buttons);
        SetColor(initial);
    }

    private void ReadChannels()
    {
        if (updating) return;
        var bytes = new byte[4];
        for (int i = 0; i < 4; i++) if (!byte.TryParse(channels[i].Text, out bytes[i])) { error.Text = "RGB・不透明度は0～255で指定してください。"; return; }
        SetColor(Color.FromArgb(bytes[3], bytes[0], bytes[1], bytes[2]), false, false, true);
    }
    private void FromField() => SetColor(FromHsv(hue.Value, field.Saturation, field.Value, SelectedColor.A), true);
    private void SetColor(Color color, bool keepHsv = false, bool keepHex = false, bool keepChannels = false)
    {
        updating = true; SelectedColor = color;
        if (!keepHsv)
        {
            double max = Math.Max(color.R, Math.Max(color.G, color.B)), min = Math.Min(color.R, Math.Min(color.G, color.B)), delta = max - min;
            if (delta > 0) hue.Value = ((max == color.R ? (color.G - color.B) / delta : max == color.G ? (color.B - color.R) / delta + 2 : (color.R - color.G) / delta + 4) * 60 + 360) % 360;
            field.Hue = hue.Value; field.Saturation = max == 0 ? 0 : delta / max; field.Value = max / 255;
        }
        field.InvalidateVisual(); alpha.Value = color.A;
        preview.Child = new Border { Background = new SolidColorBrush(color) };
        if (!keepHex) hex.Text = Format(color);
        if (!keepChannels) { byte[] parts = [color.R, color.G, color.B, color.A]; for (int i = 0; i < 4; i++) channels[i].Text = parts[i].ToString(CultureInfo.InvariantCulture); }
        error.Text = ""; updating = false;
    }
    public static string Format(Color color) => color.A == 255 ? $"#{color.R:X2}{color.G:X2}{color.B:X2}" : color.ToString();
    public static bool TryColor(string value, out Color color)
    {
        try { color = (Color)ColorConverter.ConvertFromString(value); return true; }
        catch { color = Colors.Black; return false; }
    }
    public static Color FromHsv(double hue, double saturation, double value, byte alpha = 255)
    {
        double h = (hue % 360 + 360) % 360 / 60, c = value * saturation, x = c * (1 - Math.Abs(h % 2 - 1)), m = value - c;
        (double r, double g, double b) = h switch { < 1 => (c, x, 0d), < 2 => (x, c, 0d), < 3 => (0d, c, x), < 4 => (0d, x, c), < 5 => (x, 0d, c), _ => (c, 0d, x) };
        return Color.FromArgb(alpha, (byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
    public static Border Swatch()
    {
        var tile = new DrawingGroup(); using (var dc = tile.Open())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 12, 12));
            dc.DrawRectangle(Brushes.LightGray, null, new Rect(0, 0, 6, 6)); dc.DrawRectangle(Brushes.LightGray, null, new Rect(6, 6, 6, 6));
        }
        return new Border { Width = 96, Height = 46, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Background = new DrawingBrush(tile) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 12, 12) } };
    }
}

public sealed class SaturationValueBox : FrameworkElement
{
    public double Hue { get; set; }
    public double Saturation { get; set; }
    public double Value { get; set; } = 1;
    public event Action? Changed;
    public SaturationValueBox()
    {
        Width = Height = 260; Cursor = Cursors.Cross;
        MouseLeftButtonDown += (_, e) => { CaptureMouse(); Pick(e.GetPosition(this)); e.Handled = true; };
        MouseMove += (_, e) => { if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) Pick(e.GetPosition(this)); };
        MouseLeftButtonUp += (_, _) => ReleaseMouseCapture();
    }
    private void Pick(Point p) { Saturation = Math.Clamp(p.X / ActualWidth, 0, 1); Value = 1 - Math.Clamp(p.Y / ActualHeight, 0, 1); Changed?.Invoke(); InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        var rect = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRectangle(new LinearGradientBrush(Colors.White, ColorPicker.FromHsv(Hue, 1, 1), 0), null, rect);
        dc.DrawRectangle(new LinearGradientBrush(Colors.Transparent, Colors.Black, 90), null, rect);
        var p = new Point(Saturation * ActualWidth, (1 - Value) * ActualHeight);
        dc.DrawEllipse(null, new Pen(Brushes.Black, 3), p, 5, 5); dc.DrawEllipse(null, new Pen(Brushes.White, 1), p, 5, 5);
    }
}
