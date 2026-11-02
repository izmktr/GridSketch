using System.Windows;

namespace GridSketch;

public partial class MainWindow
{
    private void ObjectLayoutProperties(SceneObject node)
    {
        var regions = document.ParentRegions(node);
        string Describe(Rect? rect) => rect is { } r ? $"X {r.X:0.##}, Y {r.Y:0.##} / {r.Width:0.##} × {r.Height:0.##}" : "なし";
        Label("親の配置範囲");
        Label("外側：" + Describe(regions.Outer)); Label("内側：" + Describe(regions.Inner));
        if (node.Kind != ObjectKind.CellSelection) Label(regions.Description);
        if (node.PreservedTransform is { } p)
        {
            Label($"親変更時の拡縮を保持：横 {Math.Sqrt(p.M11 * p.M11 + p.M12 * p.M12):0.###} 倍 / 縦 {Math.Sqrt(p.M21 * p.M21 + p.M22 * p.M22):0.###} 倍");
            Button("保持した拡縮を解除", () => Change(() => { RequireEditable(node); node.PreservedTransform = null; }));
        }
        if (node.Kind == ObjectKind.Canvas) return;
        if (node.IsGridContent) { if (node.Kind != ObjectKind.CellSelection) Label("配置はグリッドのマス・辺に従います。"); return; }
        if (node.Kind == ObjectKind.Group && node.OrganizeOnly) { Label("整理用グループは親の配置範囲を子へそのまま渡します。"); return; }
        if (document.SelectionOwner(node) is { UseObjectLayout: false })
        {
            Label("マス選択を「親の配置範囲を使う」に変更すると、共通の配置設定が使えます。"); return;
        }
        if (regions.Outer is null)
        {
            Label("親の大きさが未定義のため、親に合わせる拡縮は行いません。自身の位置とサイズで配置します。");
            return;
        }
        Check("親の範囲に合わせて配置", node.UseParentLayout, v =>
        {
            node.UseParentLayout = v;
            if (v && node.Kind == ObjectKind.Group) node.HasLayoutFrame = true;
        });
        Choice("配置先", node.LayoutRegion, regions.Inner is null ? ["親の外側"] : ["親の外側", "親の内側"], v => node.LayoutRegion = v);
        Choice("サイズの扱い", node.LayoutSizing, ["固定", "収める", "広げる"], v => node.LayoutSizing = v);
        Choice("位置合わせ", node.LayoutAnchor, ["左上", "上", "右上", "左", "中央", "右", "左下", "下", "右下"], v => node.LayoutAnchor = v);
        Label("固定：自身の大きさを維持。収める：縦横比を維持して拡縮。広げる：縦横を配置先に合わせます。位置X/Yは位置合わせからのずらし量です。");
    }
    private void InitializeObjectLayout(SceneObject node, SceneObject parent)
    {
        if (node.Kind == ObjectKind.Group) { node.HasLayoutFrame = true; node.Width = node.Height = 100; }
        if (parent.Kind == ObjectKind.CellSelection && parent.UseObjectLayout)
        {
            node.UseParentLayout = true; node.LayoutRegion = "親の内側"; node.LayoutSizing = "収める"; node.LayoutAnchor = "中央";
        }
    }
    private void InitializeWrappingFrame(SceneObject group, SceneObject node)
    {
        if (!node.UseParentLayout && document.SelectionOwner(node) is not { UseObjectLayout: true }) return;
        var regions = document.ParentRegions(node);
        if (regions.Outer is not { } outer) return;
        group.HasLayoutFrame = true; group.Width = outer.Width; group.Height = outer.Height;
        group.X = outer.X; group.Y = outer.Y;
        if (regions.Inner is { } inner)
        {
            group.LayoutHasInner = true; group.LayoutInsetX = inner.X - outer.X; group.LayoutInsetY = inner.Y - outer.Y;
        }
    }
}
