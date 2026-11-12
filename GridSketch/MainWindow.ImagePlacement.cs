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
    private void ImageSizeProperties(SceneObject node)
    {
        var header = new WrapPanel { Margin = new Thickness(0, 6, 0, 2) };
        header.Children.Add(new TextBlock { Text = "画像の大きさ", VerticalAlignment = VerticalAlignment.Center });
        var keep = new CheckBox { Content = "縦横比を保存", IsChecked = node.KeepImageAspectRatio,
            Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(keep, "縦横比を保存");
        keep.Click += (_, _) => ImmediateProperty(keep, () => node.KeepImageAspectRatio = keep.IsChecked == true);
        header.Children.Add(keep); Properties.Children.Add(header);
        if (node.ImageData is { } data)
        {
            var bitmap = Document.DecodeImage(data);
            Label($"オリジナルサイズ：{bitmap.PixelWidth} × {bitmap.PixelHeight} px");
        }
        NumericPair(Properties, "幅（px）", node.Width, "高さ（px）", node.Height, out var width, out var height, 1, 10000);
        void Resize(bool horizontal)
        {
            double value = double.Parse((horizontal ? width : height).Text, System.Globalization.CultureInfo.CurrentCulture);
            double other = node.KeepImageAspectRatio
                ? value * (horizontal ? node.Height / node.Width : node.Width / node.Height)
                : horizontal ? node.Height : node.Width;
            if (!double.IsFinite(value) || !double.IsFinite(other) || value < 1 || other < 1 || value > 100000 || other > 100000)
                throw new InvalidOperationException("画像の幅・高さは1～100000pxにしてください。");
            if (horizontal) { node.Width = value; node.Height = other; }
            else { node.Height = value; node.Width = other; }
            if (node.KeepImageAspectRatio)
                (horizontal ? height : width).Text = other.ToString(System.Globalization.CultureInfo.CurrentCulture);
        }
        LiveText(width, () => Resize(true)); LiveText(height, () => Resize(false));
    }

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
