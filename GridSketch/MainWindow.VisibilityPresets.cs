using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace GridSketch;

public partial class MainWindow
{
    private void VisibilityPresetsClick(object sender, RoutedEventArgs e)
    {
        CommitPropertyEdit();
        CreateVisibilityPresetsMenu((Button)sender).IsOpen = true;
    }
    private ContextMenu CreateVisibilityPresetsMenu(Button button)
    {
        var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };
        void Add(string title, Action action, ItemsControl? parent = null)
        {
            var item = new MenuItem { Header = new TextBlock { Text = title } };
            item.Click += (_, _) => Try(action); (parent ?? menu).Items.Add(item);
        }
        Add("全て表示", () => Change(() => document.ApplyVisibility(null)));
        foreach (var preset in document.VisibilityPresets)
            Add(preset.Name, () => Change(() => document.ApplyVisibility(preset)));
        menu.Items.Add(new Separator());
        var register = new MenuItem { Header = "登録／上書き" };
        menu.Items.Add(register);
        Add("新規登録…", () =>
        {
            if (VisibilityPresetDialog.AskName(this, "現在の表示を登録") is { } name)
                Change(() => document.VisibilityPresets.Add(document.CaptureVisibility(name)));
        }, register);
        if (document.VisibilityPresets.Count > 0)
        {
            register.Items.Add(new Separator());
            foreach (var preset in document.VisibilityPresets)
                Add($"「{preset.Name}」を上書き", () =>
                {
                    var hidden = document.CaptureVisibility(preset.Name).HiddenIds;
                    if (preset.HiddenIds.SetEquals(hidden))
                    {
                        Status.Text = $"「{preset.Name}」は現在の表示と同じ設定です。";
                        return;
                    }
                    Change(() => preset.HiddenIds = hidden);
                    if (document.VisibilityPresets.Contains(preset))
                        Status.Text = $"「{preset.Name}」を現在の表示で上書きしました。Ctrl+Zで戻せます。";
                }, register);
        }
        Add("編集…", () =>
        {
            var editor = new VisibilityPresetDialog(document) { Owner = this };
            if (editor.ShowDialog() == true) Change(() => document.VisibilityPresets = editor.Presets);
        });
        return menu;
    }
}
