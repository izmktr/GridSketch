using System.Windows.Controls;

namespace GridSketch;

public partial class MainWindow
{
    private void GroupProperties(SceneObject node)
    {
        Label("グループの用途：" + (node.OrganizeOnly ? "整理用" : "一括変形用"));
        if (!node.OrganizeOnly) return;
        bool blocked = SceneDocument.RequiresGrid(node);
        Label(blocked ? "罫線やグリッド文字などが含まれているため、一括変形用には戻せません。対象をグループの外へ移動してください。"
            : "表示・ロックをまとめて管理します。一括変形用に戻すと移動・回転できます。");
        Button("一括変形用に戻す", () => Change(() => document.EnableGroupTransform(node)));
        ((Button)Properties.Children[Properties.Children.Count - 1]).IsEnabled = !blocked && document.Editable(node);
    }
}
