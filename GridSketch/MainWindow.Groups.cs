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
            Label("子の位置・サイズは自身の配置範囲を基準にします。子を追加・変更しても、この範囲は自動で変わりません。");
            return;
        }
        bool blocked = SceneDocument.RequiresGrid(node);
        Label(blocked ? "罫線やグリッド文字などが含まれているため、一括変形用には戻せません。対象をグループの外へ移動してください。"
            : "表示・ロックをまとめて管理します。一括変形用に戻すと移動・回転できます。");
        Button("一括変形用に戻す", () => Change(() => document.EnableGroupTransform(node)));
        ((Button)Properties.Children[Properties.Children.Count - 1]).IsEnabled = !blocked && document.Editable(node);
    }
}
