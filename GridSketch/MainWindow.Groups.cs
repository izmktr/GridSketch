using System.Windows.Controls;

namespace GridSketch;

public partial class MainWindow
{
    private void GroupProperties(SceneObject node)
    {
        Label("グループの用途：" + (node.OrganizeOnly ? "整理用" : "一括変形用"));
        if (!node.OrganizeOnly)
        {
            Check("固定の配置範囲を使う", node.HasLayoutFrame, v => node.HasLayoutFrame = v || node.UseParentLayout);
            Pair("自身の幅（px）", node.Width, v => node.Width = v, "自身の高さ（px）", node.Height, v => node.Height = v, 1, 10000);
            Check("子に内側の範囲も渡す", node.LayoutHasInner, v => node.LayoutHasInner = v);
            Pair("内側の余白・左右（px）", node.LayoutInsetX, v => node.LayoutInsetX = v,
                "内側の余白・上下（px）", node.LayoutInsetY, v => node.LayoutInsetY = v, 0, 1000);
            return;
        }
        bool blocked = SceneDocument.RequiresGrid(node);
        if (blocked) Label("一括変形に変更不可：グリッド依存の子を含みます。");
        Button("一括変形用に戻す", () => Change(() => document.EnableGroupTransform(node)));
        ((Button)Properties.Children[Properties.Children.Count - 1]).IsEnabled = !blocked && document.Editable(node);
    }
}
