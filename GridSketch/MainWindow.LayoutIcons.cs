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
    private void RegionIcons(string label, string value, string outer, string inner, Action<string> setter, bool hasInner = true)
        => LayoutIcons(label, value, hasInner ? [(outer, outer), (inner, inner)] : [(outer, outer)], setter, true);

    private void SizeIcons(string label, string value, Action<string> setter, bool template = false)
        => LayoutIcons(label, value,
            [(template ? "固定サイズ" : "固定", "原寸"), ("収める", "形を保って最大"), (template ? "親いっぱい" : "広げる", "広げて最大")], setter, false);

    private void LayoutIcons(string label, string value, (string Value, string Title)[] options, Action<string> setter, bool region)
    {
        Label(label);
        var row = new WrapPanel { Margin = new Thickness(0, 3, 0, 8) };
        Properties.Children.Add(row);
        for (int i = 0; i < options.Length; i++)
        {
            var option = options[i];
            var button = new ToggleButton { Width = 72, Height = 48, Margin = new Thickness(0, 0, 8, 0),
                Style = (Style)FindResource("IconToggleStyle"), IsChecked = value == option.Value,
                ToolTip = label + "：" + option.Title, Tag = option.Value };
            AutomationProperties.SetName(button, label + "：" + option.Title);
            var icon = new Canvas { Width = 68, Height = 44, IsHitTestVisible = false };
            void Add(Shape shape, double x, double y, double width, double height, bool emphasized)
            {
                shape.Width = width; shape.Height = height;
                shape.StrokeThickness = emphasized ? 2.5 : 1;
                if (!emphasized) shape.StrokeDashArray = new DoubleCollection { 1, 2 };
                shape.SetBinding(Shape.StrokeProperty, new Binding(nameof(Control.Foreground)) { Source = button });
                Canvas.SetLeft(shape, x); Canvas.SetTop(shape, y); icon.Children.Add(shape);
            }
            if (region)
            {
                Add(new Rectangle(), 7, 6, 54, 32, i == 0);
                Add(new Rectangle(), 17, 14, 34, 16, i == 1);
            }
            else
            {
                Add(new Rectangle(), 8, 6, 52, 32, false);
                double width = i == 0 ? 16 : i == 1 ? 32 : 52;
                double height = i == 0 ? 16 : 32;
                Add(new Ellipse(), (68 - width) / 2, (44 - height) / 2, width, height, true);
            }
            button.Content = icon;
            button.Click += (_, _) =>
            {
                ImmediateProperty(button, () => setter(option.Value));
                foreach (var sibling in row.Children.OfType<ToggleButton>()) sibling.IsChecked = ReferenceEquals(sibling, button);
            };
            row.Children.Add(button);
        }
    }
}
