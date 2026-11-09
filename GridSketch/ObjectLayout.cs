using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Text.Json.Serialization;

namespace GridSketch;

public readonly record struct LayoutRegions(Rect? Outer, Rect? Inner, string Description);
public sealed class PreservedTransform
{
    public double M11 { get; set; } = 1;
    public double M12 { get; set; }
    public double M21 { get; set; }
    public double M22 { get; set; } = 1;
    [JsonIgnore] public Matrix Matrix => new(M11, M12, M21, M22, 0, 0);
}

public sealed partial class SceneDocument
{
    public LayoutRegions ParentRegions(SceneObject node)
        => RegionsForChild(Parent(node), node);

    private LayoutRegions RegionsForChild(SceneObject? parent, SceneObject node)
    {
        if (parent is null) return new(null, null, "親はありません。");
        if (parent.Kind == ObjectKind.Group && parent.OrganizeOnly) return ParentRegions(parent);
        if (parent.Kind == ObjectKind.Canvas)
            return parent.AutoSize ? new(null, null, "自動サイズのキャンバス：内容から大きさを決めます。")
                : new(new Rect(parent.X, parent.Y, parent.CanvasPixelWidth, parent.CanvasPixelHeight), null, "固定サイズのキャンバス");
        if (parent.Kind == ObjectKind.Group)
        {
            if (!parent.HasLayoutFrame) return new(null, null, "このグループは従来方式です。親グループで固定の配置範囲を有効にしてください。");
            var outer = new Rect(0, 0, parent.Width, parent.Height);
            Rect? inner = parent.LayoutHasInner ? new Rect(parent.LayoutInsetX, parent.LayoutInsetY,
                Math.Max(0, parent.Width - 2 * parent.LayoutInsetX), Math.Max(0, parent.Height - 2 * parent.LayoutInsetY)) : null;
            return new(outer, inner, "親グループ自身の配置範囲（拡縮前）");
        }
        if (parent.IsStateSelector && GridFor(parent) is { } cells)
            return CellRegions(cells, new(0, 0), "マス切り替えから渡される1マスの範囲");
        if (parent.Kind == ObjectKind.Grid)
        {
            if (node.IsGridContent)
                return CellRegions(parent, node.Kind == ObjectKind.Image ? new(node.Column, node.Row) : new(0, 0), "対象マスごとに渡される範囲（表示は1マス分）");
            return new(new Rect(0, 0, parent.Columns * parent.CellSize, parent.Rows * parent.CellSize), null,
                "自由配置用のグリッド全体。マスごとの配置はマス切り替えを使用します。");
        }
        return new(null, null, "親は配置範囲を持ちません。");
    }
    public double InitialSquareSize(SceneObject parent)
    {
        // Organizing groups pass their parent's space through without defining a frame.
        while (parent.Kind == ObjectKind.Group && parent.OrganizeOnly && Parent(parent) is { } ancestor)
            parent = ancestor;
        var regions = parent.Kind == ObjectKind.Grid
            ? CellRegions(parent, new(0, 0), "")
            : RegionsForChild(parent, new SceneObject { Kind = ObjectKind.Shape });
        var region = regions.Inner ?? regions.Outer;
        return region is { Width: > 0, Height: > 0 } box
            ? Math.Max(1, Math.Min(box.Width, box.Height)) : 100;
    }
    private static LayoutRegions CellRegions(SceneObject grid, CellPosition cell, string description) =>
        new(CellRect(grid, cell, false), CellRect(grid, cell, true), description);

    public static Rect OwnLayoutRect(SceneObject node)
    {
        if (node.Kind == ObjectKind.Grid) return new(0, 0, node.Columns * node.CellSize, node.Rows * node.CellSize);
        if (node.Kind == ObjectKind.Text)
        {
            var bounds = SceneRenderer.StyledText(node, node.Text, node.Font, node.FontSize).Bounds;
            return bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0 ? new Rect(0, 0, 1, 1) : bounds;
        }
        if (node.Kind == ObjectKind.Line)
        {
            var bounds = new Rect(new Point(0, 0), new Point(node.EndX, node.EndY));
            if (node.PathKind != "直線") bounds.Union(new Point(node.ControlX, node.ControlY));
            bounds.Inflate(Math.Max(0.5, node.Thickness / 2), Math.Max(0.5, node.Thickness / 2));
            return bounds;
        }
        return new Rect(0, 0, node.Width, node.Height);
    }
    public bool ObjectLayoutActive(SceneObject node) => node.UseParentLayout && !node.IsGridContent &&
        node.Kind != ObjectKind.Canvas && !(node.Kind == ObjectKind.Group && node.OrganizeOnly) &&
        (SelectionOwner(node) is not { } owner || owner.UseObjectLayout);

    public Matrix EffectiveLocalMatrix(SceneObject node)
    {
        var matrix = LocalMatrix(node);
        if (!ObjectLayoutActive(node)) return matrix;
        var regions = ParentRegions(node);
        var target = node.LayoutRegion == "親の内側" ? regions.Inner : regions.Outer;
        // Auto-size canvases cannot depend on their children fitting their own result.
        if (target is not { Width: > 0, Height: > 0 } box) return matrix;
        matrix.Translate(-node.X, -node.Y);
        var bounds = new MatrixTransform(matrix).TransformBounds(OwnLayoutRect(node));
        double sx = 1, sy = 1;
        if (node.LayoutSizing == "収める") sx = sy = Math.Min(box.Width / bounds.Width, box.Height / bounds.Height);
        else if (node.LayoutSizing == "広げる") { sx = box.Width / bounds.Width; sy = box.Height / bounds.Height; }
        double ax = node.LayoutAnchor.Contains('左') ? 0 : node.LayoutAnchor.Contains('右') ? 1 : 0.5;
        double ay = node.LayoutAnchor.Contains('上') ? 0 : node.LayoutAnchor.Contains('下') ? 1 : 0.5;
        matrix.Scale(sx, sy);
        matrix.Translate(box.X + (box.Width - bounds.Width * sx) * ax - bounds.X * sx + node.X,
            box.Y + (box.Height - bounds.Height * sy) * ay - bounds.Y * sy + node.Y);
        return matrix;
    }
    private void ValidateObjectLayout(SceneObject node)
    {
        if (node.PreservedTransform is { } p && (!double.IsFinite(p.M11) || !double.IsFinite(p.M12) || !double.IsFinite(p.M21) || !double.IsFinite(p.M22) || !p.Matrix.HasInverse))
            throw new InvalidDataException("親変更時の変形が不正です。");
        if (node.LayoutRegion is not ("親の外側" or "親の内側") || node.LayoutSizing is not ("固定" or "収める" or "広げる") ||
            node.LayoutAnchor is not ("左上" or "上" or "右上" or "左" or "中央" or "右" or "左下" or "下" or "右下"))
            throw new InvalidDataException("親の配置設定が不正です。");
        if (!double.IsFinite(node.LayoutInsetX) || !double.IsFinite(node.LayoutInsetY) || node.LayoutInsetX < 0 || node.LayoutInsetY < 0 ||
            node.Kind == ObjectKind.Group && node.HasLayoutFrame && node.LayoutHasInner && (node.LayoutInsetX * 2 >= node.Width || node.LayoutInsetY * 2 >= node.Height))
            throw new InvalidDataException("内側の余白は自身のサイズの半分未満にしてください。");
        if (ObjectLayoutActive(node))
        {
            if (node.Kind == ObjectKind.Group && !node.HasLayoutFrame) throw new InvalidDataException("親に合わせるグループには固定の配置範囲が必要です。");
            var regions = ParentRegions(node);
            if (regions.Outer is not null && node.LayoutRegion == "親の内側" && regions.Inner is null)
                throw new InvalidDataException("親は内側の範囲を持ちません。「親の外側」を選択してください。");
        }
    }
}
