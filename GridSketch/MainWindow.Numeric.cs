using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace GridSketch;

public partial class MainWindow
{
    private static double[] SliderValues(double minimum, double maximum)
    {
        var values = new SortedSet<double> { minimum, maximum };
        if (minimum == 0)
            values.UnionWith(new[] { 0.25, 0.5, 0.75, 1, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10 });
        else
            values.UnionWith(Enumerable.Range(1, 11).Select(n => (double)n));
        for (double factor = 1; 12 * factor <= maximum; factor *= 2)
            foreach (double basis in new[] { 12d, 16, 20 }) values.Add(basis * factor);
        return values.Where(v => v >= minimum && v <= maximum).ToArray();
    }

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
        var choices = SliderValues(min, maximum);
        // A small offset keeps zero available for strokes while retaining logarithmic spacing.
        double offset = min == 0 ? 0.25 : 0;
        double Position(double number) => Math.Log2(Math.Clamp(number, min, maximum) + offset);
        var ticks = choices.Select(Position).ToArray();
        var slider = new Slider { Minimum = ticks[0], Maximum = ticks[^1], Value = Position(value), SmallChange = 0.001, LargeChange = 1,
            Ticks = new DoubleCollection(ticks), IsSnapToTickEnabled = true, IsTabStop = false,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 3, 0, 8), MinWidth = 35 };
        AutomationProperties.SetName(slider, label + " スライダー");
        System.Windows.Input.KeyboardNavigation.SetTabNavigation(slider, System.Windows.Input.KeyboardNavigationMode.None);
        Grid.SetColumn(slider, 1); row.Children.Add(slider);
        bool synchronizing = false;
        input.TextChanged += (_, _) =>
        {
            if (synchronizing || !double.TryParse(input.Text, out var number) || !double.IsFinite(number)) return;
            synchronizing = true;
            slider.Value = Position(number);
            slider.ToolTip = number.ToString(CultureInfo.CurrentCulture);
            synchronizing = false;
        };
        slider.ValueChanged += (_, _) =>
        {
            if (synchronizing) return;
            synchronizing = true;
            int index = Enumerable.Range(0, ticks.Length).MinBy(i => Math.Abs(ticks[i] - slider.Value));
            slider.Value = ticks[index];
            input.Text = choices[index].ToString(CultureInfo.CurrentCulture);
            slider.ToolTip = input.Text;
            synchronizing = false;
        };
        slider.ToolTip = input.Text;
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
        LiveText(a, () => setFirst(Parse(a.Text))); LiveText(b, () => setSecond(Parse(b.Text)));
    }
}
