using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace GridSketch;

public partial class MainWindow
{
    private static FrameworkElement NumericEditor(string label, double value, out TextBox box, double? minimum = null, double maximum = 1000)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 2) });
        var row = new Grid(); panel.Children.Add(row);
        var input = new TextBox { Text = value.ToString(CultureInfo.CurrentCulture), MinWidth = 48, Margin = new Thickness(0, 3, 0, 8), Padding = new Thickness(4) };
        box = input; AutomationProperties.SetName(input, label);
        row.Children.Add(input);
        if (minimum is not double min) return panel;
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var slider = new Slider { Minimum = min, Maximum = maximum, Value = Math.Clamp(value, min, maximum), SmallChange = 1, LargeChange = 10,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 3, 0, 8), MinWidth = 35 };
        AutomationProperties.SetName(slider, label + " スライダー");
        Grid.SetColumn(slider, 1); row.Children.Add(slider);
        bool synchronizing = false;
        input.TextChanged += (_, _) =>
        {
            if (synchronizing || !double.TryParse(input.Text, out var number) || !double.IsFinite(number)) return;
            synchronizing = true;
            slider.Value = Math.Clamp(number, min, maximum);
            synchronizing = false;
        };
        slider.ValueChanged += (_, _) =>
        {
            if (synchronizing) return;
            synchronizing = true;
            input.Text = Math.Round(slider.Value, 1).ToString(CultureInfo.CurrentCulture);
            synchronizing = false;
        };
        slider.ToolTip = $"{min}～{maximum}（範囲外の値は数値欄で入力）";
        return panel;
    }

    private static void NumericPair(StackPanel panel, string firstLabel, double first, string secondLabel, double second,
        out TextBox firstBox, out TextBox secondBox, double? minimum = null, double maximum = 1000)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var left = NumericEditor(firstLabel, first, out firstBox, minimum, maximum);
        var right = NumericEditor(secondLabel, second, out secondBox, minimum, maximum);
        Grid.SetColumn(right, 2); row.Children.Add(left); row.Children.Add(right); panel.Children.Add(row);
    }

    private void Pair(string firstLabel, double first, Action<double> setFirst, string secondLabel, double second, Action<double> setSecond,
        double? minimum = null, double maximum = 1000, bool integer = false)
    {
        NumericPair(Properties, firstLabel, first, secondLabel, second, out var a, out var b, minimum, maximum);
        double Parse(string text) => integer ? int.Parse(text, CultureInfo.CurrentCulture) : double.Parse(text, CultureInfo.CurrentCulture);
        setters.Add(() => setFirst(Parse(a.Text))); setters.Add(() => setSecond(Parse(b.Text)));
    }
}
