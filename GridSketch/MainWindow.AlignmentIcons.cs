using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Shapes;

namespace GridSketch;

public partial class MainWindow
{
    private void AlignmentIcons(string label, string value, Action<string> setter)
    {
        Label(label);
        var buttons = new UniformGrid { Columns = 3, Rows = 3, Width = 120, Height = 120,
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 3, 0, 8) };
        Properties.Children.Add(buttons);
        foreach (var direction in new[] { "左上", "上", "右上", "左", "中央", "右", "左下", "下", "右下" })
        {
            var button = new ToggleButton { Style = (Style)FindResource("IconToggleStyle"), Width = 36, Height = 36,
                Margin = new Thickness(2), IsChecked = direction == value, ToolTip = direction, Tag = direction };
            AutomationProperties.SetName(button, label + "：" + direction);
            var icon = new Grid { Width = 24, Height = 24, IsHitTestVisible = false };
            var lines = new Canvas(); icon.Children.Add(lines);
            void Edge(double x1, double y1, double x2, double y2)
            {
                var line = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, StrokeThickness = 1.4 };
                line.SetBinding(Shape.StrokeProperty, new Binding("Foreground") { Source = button }); lines.Children.Add(line);
            }
            if (direction.Contains('左') || direction == "中央") Edge(1, 1, 1, 23);
            if (direction.Contains('右') || direction == "中央") Edge(23, 1, 23, 23);
            if (direction.Contains('上') || direction == "中央") Edge(1, 1, 23, 1);
            if (direction.Contains('下') || direction == "中央") Edge(1, 23, 23, 23);
            icon.Children.Add(new TextBlock { Text = "a", FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center });
            button.Content = icon; buttons.Children.Add(button);
            button.Click += (_, _) =>
            {
                ImmediateProperty(button, () => setter(direction));
                foreach (var sibling in buttons.Children.OfType<ToggleButton>()) sibling.IsChecked = Equals(sibling.Tag, direction);
            };
        }
    }
}
