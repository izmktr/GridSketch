using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace GridSketch;

public partial class MainWindow
{
    private PropertySectionState propertySectionState = PropertySectionState.Shared;
    private readonly List<Action> propertySectionSummaries = [];
    private sealed record PropertySection(string Title, int Start, bool Initial, Func<string>? Summary);

    private void WrapPropertySections(SceneObject node, List<PropertySection> sections)
    {
        int end = Properties.Children.Count;
        foreach (var section in sections.AsEnumerable().Reverse())
        {
            int count = end - section.Start;
            end = section.Start;
            if (count == 0) continue;
            var body = new StackPanel { Margin = new Thickness(6, 2, 6, 8) };
            for (int i = 0; i < count; i++)
            {
                var child = Properties.Children[section.Start];
                Properties.Children.RemoveAt(section.Start);
                body.Children.Add(child);
            }
            string kind = node.Kind == ObjectKind.Border && node.ConnectCellCenters ? "CenterBorder" : node.Kind.ToString();
            string key = kind + "/" + section.Title;
            var arrow = new TextBlock { Width = 18, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
            var summary = new TextBlock { Foreground = Brushes.DimGray, Margin = new Thickness(8, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            var header = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(arrow, Dock.Right); header.Children.Add(arrow);
            var title = new TextBlock { Text = section.Title, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(title, Dock.Left); header.Children.Add(title); header.Children.Add(summary);
            var toggle = new ToggleButton { Content = header, IsChecked = propertySectionState.IsOpen(key, section.Initial),
                HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8, 7, 8, 7),
                Margin = new Thickness(0), Background = new SolidColorBrush(Color.FromRgb(237, 241, 245)),
                BorderThickness = new Thickness(0) };
            AutomationProperties.SetName(toggle, section.Title + "の開閉");
            void Update()
            {
                bool open = toggle.IsChecked == true;
                body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                arrow.Text = open ? "▼" : "▶";
                summary.Text = open ? "" : section.Summary?.Invoke() ?? "";
                toggle.ToolTip = open ? section.Title + "を閉じる" : section.Title + "を開く";
            }
            toggle.Click += (_, _) =>
            {
                CommitPropertyEdit();
                propertySectionState.Set(key, toggle.IsChecked == true);
                Update();
            };
            propertySectionSummaries.Add(Update); Update();
            var container = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            container.Children.Add(toggle); container.Children.Add(body);
            Properties.Children.Insert(section.Start, container);
        }
    }
}
