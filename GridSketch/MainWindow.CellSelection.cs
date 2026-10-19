using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GridSketch;

public partial class MainWindow
{
    private sealed record StateTarget(string? Id, string Name);
    private void CellSelectionProperties(SceneObject node)
    {
        Label("空 → 1 → 2 → … → 空。クリックで切り替え、ドラッグで同じ状態を配置します。番号は切り替え順です。");
        Label("表示する見本は直下の子から指定します。画像・グループなどを追加すると新しい状態へ自動で割り当てます。");
        var targets = new List<StateTarget> { new(null, "未割り当て") };
        targets.AddRange(node.Children.Select(c => new StateTarget(c.Id, c.Name + " [" + SceneObject.KindName(c.Kind) + "]")));
        for (int i = 0; i < node.States.Count; i++)
        {
            var state = node.States[i]; int index = i;
            Label($"{i + 1}：状態名");
            var name = new TextBox { Text = state.Name }; Properties.Children.Add(name);
            var target = new ComboBox { ItemsSource = targets, DisplayMemberPath = "Name", SelectedItem = targets.FirstOrDefault(t => t.Id == state.ObjectId) ?? targets[0] };
            System.Windows.Automation.AutomationProperties.SetName(target, $"状態{i + 1}の表示オブジェクト");
            Properties.Children.Add(target);
            setters.Add(() => { state.Name = name.Text; state.ObjectId = (target.SelectedItem as StateTarget)?.Id; });
            var controls = new WrapPanel(); Properties.Children.Add(controls);
            void ActionButton(string title, Action action, bool enabled = true)
            {
                var button = new Button { Content = title, IsEnabled = enabled && document.Editable(node), Padding = new Thickness(8, 4, 8, 4) };
                button.Click += (_, _) => Change(() => { RequireEditable(node); action(); }); controls.Children.Add(button);
            }
            ActionButton("↑", () => { node.States.Remove(state); node.States.Insert(index - 1, state); }, i > 0);
            ActionButton("↓", () => { node.States.Remove(state); node.States.Insert(index + 1, state); }, i + 1 < node.States.Count);
            ActionButton("状態を削除", () => { node.States.Remove(state); node.StateCells.RemoveAll(c => c.StateId == state.Id); });
        }
        Button("状態を追加", () => Change(() => { RequireEditable(node); node.States.Add(new CellState { Name = $"状態 {node.States.Count + 1}" }); }));
        Label("名前・割り当ては「プロパティを適用」で反映します。状態を削除すると、その状態のマスは空になります。見本の子を削除した場合は未割り当てになります。");
    }

    private void TemplateProperties(SceneObject node)
    {
        if (document.SelectionOwner(node) is not { } owner) return;
        Label("マス選択の見本です。通常の位置には描画せず、割り当てたマスの余白内へ縮小・中央配置します。グループ内の位置関係は保持します。");
        var drawing = Surface.Renderer!.Template(node);
        if (!drawing.Bounds.IsEmpty)
            Properties.Children.Add(new Image { Source = new DrawingImage(drawing), Height = 90, Stretch = Stretch.Uniform, Margin = new Thickness(8) });
        Button("マス選択の割り当てを開く", () => { selectedId = owner.Id; RefreshAll(); });
    }
}
