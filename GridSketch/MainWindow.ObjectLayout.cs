using System.Windows;

namespace GridSketch;

public partial class MainWindow
{
    private void ObjectLayoutProperties(SceneObject node)
    {
        if (document.UsesGridCoordinates(node))
        {
            RegionIcons("基準", node.LayoutRegion == "親の内側" ? "余白の内側" : "マス全体", "マス全体", "余白の内側",
                v => node.LayoutRegion = v == "余白の内側" ? "親の内側" : "親の外側");
            SizeIcons("サイズの扱い", node.LayoutSizing, v => node.LayoutSizing = v);
            AlignmentIcons("位置合わせ", node.LayoutAnchor, v => node.LayoutAnchor = v);
            Pair("ずらし量 X（px）", node.PlacementOffsetX, v => node.PlacementOffsetX = v,
                "ずらし量 Y（px）", node.PlacementOffsetY, v => node.PlacementOffsetY = v);
            return;
        }
        var regions = document.ParentRegions(node);
        string Describe(Rect r) => $"X {r.X:0.##}, Y {r.Y:0.##} / {r.Width:0.##} × {r.Height:0.##}";
        if (regions.Outer is not null || regions.Inner is not null) Label("親の配置範囲");
        if (regions.Outer is { } outer) Label("外側：" + Describe(outer));
        if (regions.Inner is { } inner) Label("内側：" + Describe(inner));
        if (node.Kind == ObjectKind.Canvas) return;
        if (node.IsGridContent) return;
        if (node.Kind == ObjectKind.Group && node.OrganizeOnly) return;
        if (document.SelectionOwner(node) is { UseObjectLayout: false })
        {
            return;
        }
        if (regions.Outer is null)
        {
            if (node.UseParentLayout) Label("親合わせ：停止中（親の配置範囲なし）");
            return;
        }
        Check("親の範囲に合わせて配置", node.UseParentLayout, v =>
        {
            node.UseParentLayout = v;
            if (v && node.Kind == ObjectKind.Group) node.HasLayoutFrame = true;
        });
        RegionIcons("配置先", node.LayoutRegion, "親の外側", "親の内側", v => node.LayoutRegion = v, regions.Inner is not null);
        SizeIcons("サイズの扱い", node.LayoutSizing, v => node.LayoutSizing = v);
        if (node.Kind == ObjectKind.Text) AlignmentIcons("位置合わせ", node.LayoutAnchor, v => node.LayoutAnchor = v);
        else Choice("位置合わせ", node.LayoutAnchor, ["左上", "上", "右上", "左", "中央", "右", "左下", "下", "右下"], v => node.LayoutAnchor = v);
    }
    private void InitializeObjectLayout(SceneObject node, SceneObject parent)
    {
        if (node.Kind == ObjectKind.Image && node.ImageData is { } data)
        {
            var bitmap = Document.DecodeImage(data);
            node.Width = bitmap.PixelWidth; node.Height = bitmap.PixelHeight;
        }
        if (node.Kind == ObjectKind.Shape)
            node.Width = node.Height = document.InitialSquareSize(parent);
        if (node.Kind == ObjectKind.Group) { node.HasLayoutFrame = true; node.Width = node.Height = 100; }
        if (document.CoordinateGrid(parent) is not null && SceneDocument.CanUseGridCoordinates(node))
            SceneDocument.InitializeGridCoordinates(node, (Surface.ActiveCell?.Column ?? 0) + 1, (Surface.ActiveCell?.Row ?? 0) + 1);
        if (parent.IsStateSelector && parent.UseObjectLayout)
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
