using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace GridSketch;

public sealed partial class ColorPicker
{
    private static readonly (string Name, string[] Colors)[] Themes =
    [
        ("基本色", ["#000000", "#FFFFFF", "#FF0000", "#FF8800", "#FFFF00", "#00AA44", "#0066FF", "#8800CC"]),
        ("淡い色", ["#FADADD", "#FFE2C2", "#FFF4B8", "#DAF0C5", "#C6EEE5", "#D1E6FF", "#E4D8F5", "#F6D9EC"]),
        ("濃い色", ["#761C2C", "#8C4218", "#756000", "#255B32", "#175E5B", "#203E78", "#503273", "#74285C"]),
        ("グレー", ["#FFFFFF", "#EEEEEE", "#CCCCCC", "#AAAAAA", "#808080", "#555555", "#333333", "#000000"]),
        ("くすみ色", ["#B6757C", "#BB9373", "#B4AA76", "#8FA381", "#7EA69E", "#819BB3", "#9C8BAA", "#AF8C9D"]),
        ("色分け用", ["#000000", "#E69F00", "#56B4E9", "#009E73", "#F0E442", "#0072B2", "#D55E00", "#CC79A7"])
    ];

    private void AddPalettes(StackPanel root, RecentColors history)
    {
        var heading = new DockPanel { Margin = new Thickness(0, 10, 0, 6) };
        var transparent = PaletteButton(Color.FromArgb(0, 0, 0, 0), "透明");
        transparent.Width = root.Width / 8 - transparent.Margin.Left - transparent.Margin.Right;
        transparent.ToolTip = "透明（不透明度0%）";
        DockPanel.SetDock(transparent, Dock.Right); heading.Children.Add(transparent);
        heading.Children.Add(new TextBlock { Text = "プリセット", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(heading);
        var themeRow = new UniformGrid { Columns = 6, Margin = new Thickness(0, 0, 0, 6) };
        var colorsRow = new UniformGrid { Columns = 8 };
        root.Children.Add(themeRow); root.Children.Add(colorsRow);
        void SelectTheme(int index)
        {
            foreach (var button in themeRow.Children.OfType<ToggleButton>()) button.IsChecked = (int)button.Tag == index;
            colorsRow.Children.Clear();
            foreach (var code in Themes[index].Colors)
            {
                TryColor(code, out var color);
                colorsRow.Children.Add(PaletteButton(color, Themes[index].Name + " " + code));
            }
        }
        for (int i = 0; i < Themes.Length; i++)
        {
            int index = i;
            var button = new ToggleButton { Content = Themes[i].Name, Tag = i, MinHeight = 30, Padding = new Thickness(2), Margin = new Thickness(2, 0, 2, 0) };
            System.Windows.Automation.AutomationProperties.SetName(button, "テーマ：" + Themes[i].Name);
            button.Click += (_, _) => SelectTheme(index); themeRow.Children.Add(button);
        }
        SelectTheme(0);
        root.Children.Add(new TextBlock { Text = "最近使った色", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 6) });
        var recentRow = new UniformGrid { Columns = 8 }; root.Children.Add(recentRow);
        for (int i = 0; i < 8; i++)
        {
            if (i < history.Entries.Count) recentRow.Children.Add(PaletteButton(history.Entries[i], "最近使った色 " + (i + 1) + " " + Format(history.Entries[i])));
            else recentRow.Children.Add(new Border { Height = 32, Margin = new Thickness(2, 0, 2, 0), Background = Brushes.WhiteSmoke, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), ToolTip = "履歴なし" });
        }
    }

    private Button PaletteButton(Color color, string name)
    {
        var swatch = Swatch(); swatch.Width = double.NaN; swatch.Height = 26;
        swatch.Child = new Border { Background = new SolidColorBrush(color) };
        var button = new Button { Content = swatch, Height = 32, Margin = new Thickness(2, 0, 2, 0), Padding = new Thickness(2), HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ToolTip = $"{Format(color)} / 不透明度 {color.A * 100 / 255.0:0.#}%" };
        System.Windows.Automation.AutomationProperties.SetName(button, name);
        button.Click += (_, _) => SetColor(color);
        return button;
    }
}
