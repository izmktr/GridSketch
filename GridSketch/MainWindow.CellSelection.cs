using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GridSketch;

public partial class MainWindow
{
    private sealed record StateTarget(string? Id, string Name);
    private void CellSelectionProperties(SceneObject node)
    {
        Choice("見本の配置方式", node.UseCellLayout ? "子ごとにマスへ配置" : "全体を収める（従来）", ["子ごとにマスへ配置", "全体を収める（従来）"], v => node.UseCellLayout = v == "子ごとにマスへ配置");
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
        Label(owner.UseCellLayout
            ? "マス全体と余白の内側は親グリッドから引き継ぎます。位置X/Yは配置基準からのずらし量です。"
            : "従来の配置方式です。見本全体を縮小・中央配置します。子ごとの配置設定を使うには、マス選択の配置方式を変更してください。");
        if (node.Kind == ObjectKind.Group) Label("グループは2つの配置領域を子へ引き継ぎます。位置・回転は子全体へ適用し、内容の大きさによる自動縮小はしません（子ごとの配置方式）。");
        else
        {
            Choice("配置領域", node.TemplateRegion, ["マス全体", "余白の内側"], v => node.TemplateRegion = v);
            Choice("領域への収め方", node.TemplateSizing, ["固定サイズ", "収める", "親いっぱい"], v => node.TemplateSizing = v);
            Choice("配置基準", node.TemplateAnchor, ["左上", "上", "右上", "左", "中央", "右", "左下", "下", "右下"], v => node.TemplateAnchor = v);
            Label("固定サイズは指定寸法を維持。収めるは縦横比を保って拡大縮小。親いっぱいは縦横を領域に合わせます。領域の外は切り取ります。");
        }
        var grid = document.GridFor(owner)!;
        var drawing = owner.UseCellLayout ? Surface.Renderer!.CellTemplate(node, grid) : Surface.Renderer!.Template(node);
        if (owner.UseCellLayout)
        {
            // Give the preview a fixed cell-sized frame so changing a sibling does not resize it.
            var framed = new DrawingGroup();
            using (var dc = framed.Open())
            {
                var rect = SceneDocument.CellRect(grid, new(0, 0), false);
                dc.DrawRectangle(Brushes.White, new Pen(Brushes.LightGray, 0.5), rect);
                dc.PushClip(new RectangleGeometry(rect)); dc.DrawDrawing(drawing); dc.Pop();
            }
            drawing = framed;
        }
        if (!drawing.Bounds.IsEmpty)
            Properties.Children.Add(new Image { Source = new DrawingImage(drawing), Height = 90, Stretch = Stretch.Uniform, Margin = new Thickness(8) });
        Button("マス選択の割り当てを開く", () => { selectedId = owner.Id; RefreshAll(); });
    }
}
