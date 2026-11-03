using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace GridSketch;

public partial class MainWindow
{
    private void VisibilityPresetsClick(object sender, RoutedEventArgs e)
    {
        CommitPropertyEdit();
        var menu = new ContextMenu { PlacementTarget = (Button)sender, Placement = PlacementMode.Bottom };
        void Add(string title, Action action)
        {
            var item = new MenuItem { Header = new TextBlock { Text = title } };
            item.Click += (_, _) => Try(action); menu.Items.Add(item);
        }
        Add("全て表示", () => Change(() => document.ApplyVisibility(null)));
        foreach (var preset in document.VisibilityPresets)
            Add(preset.Name, () => Change(() => document.ApplyVisibility(preset)));
        menu.Items.Add(new Separator());
        Add("現在を登録…", () =>
        {
            if (VisibilityPresetDialog.AskName(this, "現在の表示を登録") is { } name)
                Change(() => document.VisibilityPresets.Add(document.CaptureVisibility(name)));
        });
        Add("編集…", () =>
        {
            var editor = new VisibilityPresetDialog(document) { Owner = this };
            if (editor.ShowDialog() == true) Change(() => document.VisibilityPresets = editor.Presets);
        });
        menu.IsOpen = true;
    }
}
