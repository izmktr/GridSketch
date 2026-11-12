using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

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

}
