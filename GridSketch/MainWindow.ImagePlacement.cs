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
    private void ImagePlacementProperties(SceneObject node)
    {
        Label("配置方式");
        var row = new WrapPanel { Margin = new Thickness(0, 3, 0, 8) }; Properties.Children.Add(row);
        foreach (bool cells in new[] { false, true })
        {
            string title = cells ? "マス配置" : "自由配置";
            var button = new ToggleButton { Style = (Style)FindResource("IconToggleStyle"),
                IsChecked = node.SnapToCell == cells, MinWidth = 110, Height = 36, Margin = new Thickness(0, 0, 8, 0),
                IsEnabled = document.Editable(node) && (!cells || document.GridFor(node) is not null),
                ToolTip = cells ? "親グリッドのマスを基準に配置" : "親の座標で自由に配置" };
            AutomationProperties.SetName(button, "配置方式：" + title);
            var content = new StackPanel { Orientation = Orientation.Horizontal, IsHitTestVisible = false };
            var icon = new Path { Width = 20, Height = 20, Stretch = Stretch.Uniform, StrokeThickness = 1.5,
                Margin = new Thickness(0, 0, 6, 0), Data = Geometry.Parse(cells
                    ? "M1,1 L19,1 19,19 1,19 Z M10,1 L10,19 M1,10 L19,10 M7,5 A2,2 0 1 1 3,5 A2,2 0 1 1 7,5"
                    : "M10,1 L10,19 M1,10 L19,10 M6,5 L10,1 14,5 M6,15 L10,19 14,15 M5,6 L1,10 5,14 M15,6 L19,10 15,14") };
            icon.SetBinding(Shape.StrokeProperty, new Binding("Foreground") { Source = button });
            content.Children.Add(icon); content.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
            button.Content = content; row.Children.Add(button);
            button.Click += (_, _) =>
            {
                if (node.SnapToCell != cells) Change(() => document.SetImageCellPlacement(node, cells));
                else button.IsChecked = true;
            };
        }
    }
}
