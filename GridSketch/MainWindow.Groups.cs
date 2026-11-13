using System.Windows.Controls;

namespace GridSketch;

public partial class MainWindow
{
    private void GroupProperties(SceneObject node)
    {
        if (!node.OrganizeOnly)
        {
            Pair("自身の幅（px）", node.Width, v => node.Width = v, "自身の高さ（px）", node.Height, v => node.Height = v, 1, 10000);
            Check("子に内側の範囲も渡す", node.LayoutHasInner, v => node.LayoutHasInner = v);
            Pair("内側の余白・左右（px）", node.LayoutInsetX, v => node.LayoutInsetX = v,
                "内側の余白・上下（px）", node.LayoutInsetY, v => node.LayoutInsetY = v, 0, 1000);
        }
        else Label("子は親の配置範囲とマス座標を引き継ぎます。");
        var error = document.ConversionError(node, !node.OrganizeOnly);
        Button(node.IsOrganizer ? "フレームに変換" : "グループに変換",
            () => Change(() => document.ConvertContainer(node, !node.OrganizeOnly)));
        var button = (Button)Properties.Children[Properties.Children.Count - 1];
        button.IsEnabled = error is null; button.ToolTip = error;
        if (error is not null) Label(error);
    }
}
