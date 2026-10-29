using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GridSketch;

public partial class MainWindow
{
    private void StrokeIconChoice(string label, string value, string[] options, Action<string> setter, bool caps = false)
    {
        Label(label);
        var row = new WrapPanel { Margin = new Thickness(0, 3, 0, 8) };
        Properties.Children.Add(row);
        var selected = value;
        foreach (var option in options)
        {
            var button = new ToggleButton
            {
                Width = 72, Height = 48, Margin = new Thickness(0, 0, 8, 0),
                Style = (Style)FindResource("IconToggleStyle"), IsChecked = option == value,
                ToolTip = label + "：" + option, Tag = option
            };
            AutomationProperties.SetName(button, label + "：" + option);
            var icon = new Canvas { Width = 68, Height = 44, ClipToBounds = true, IsHitTestVisible = false };
            void Segment(double x1, double y1, double x2, double y2, double thickness, PenLineCap cap, DoubleCollection? dash = null)
            {
                var line = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, StrokeThickness = thickness,
                    StrokeStartLineCap = cap, StrokeEndLineCap = cap, StrokeDashArray = dash };
                line.SetBinding(Shape.StrokeProperty, new Binding(nameof(Control.Foreground)) { Source = button });
                icon.Children.Add(line);
            }
            if (caps)
            {
                var cap = option switch { "四角" => PenLineCap.Square, "丸" => PenLineCap.Round, _ => PenLineCap.Flat };
                // Separate segments expose the missing corner for flat caps. The vertical end is outside the viewport.
                Segment(18, 16, 18, 56, 10, cap);
                Segment(18, 16, 52, 16, 10, cap);
            }
            else Segment(8, 22, 60, 22, 4, PenLineCap.Flat,
                option == "破線" ? new DoubleCollection { 3, 2 } : option == "点線" ? new DoubleCollection { 1, 1.5 } : null);
            button.Content = icon;
            button.Click += (_, _) =>
            {
                selected = option;
                foreach (var sibling in row.Children.OfType<ToggleButton>()) sibling.IsChecked = ReferenceEquals(sibling, button);
            };
            row.Children.Add(button);
        }
        setters.Add(() => setter(selected));
    }
}
