using System.IO;
using System.Windows;

namespace GridSketch;

public sealed partial class SceneDocument
{
    public static bool CanUseGridCoordinates(SceneObject node) => node.Kind is ObjectKind.Image or ObjectKind.Text
        or ObjectKind.Shape or ObjectKind.Line || node.Kind == ObjectKind.Group && !node.OrganizeOnly && !RequiresGrid(node);

    public SceneObject? CoordinateGrid(SceneObject? parent)
    {
        while (parent is { Kind: ObjectKind.Group, OrganizeOnly: true }) parent = Parent(parent);
        return parent?.Kind == ObjectKind.Grid ? parent : null;
    }

    public bool UsesGridCoordinates(SceneObject node) => node.GridColumn.HasValue && node.GridRow.HasValue
        && CoordinateGrid(Parent(node)) is not null && CanUseGridCoordinates(node);

    public static void InitializeGridCoordinates(SceneObject node, double column = 1, double row = 1)
    {
        node.GridColumn = column; node.GridRow = row;
        node.X = node.Y = 0; node.SnapToCell = false;
        node.UseParentLayout = true;
        node.LayoutRegion = "親の内側"; node.LayoutAnchor = "中央"; node.LayoutSizing = "固定";
        if (node.Kind == ObjectKind.Group) node.HasLayoutFrame = true;
    }

    // Import legacy pixel placements without changing their rendered position or size.
    public void AdoptGridCoordinates(SceneObject node)
    {
        if (UsesGridCoordinates(node) || !CanUseGridCoordinates(node) || CoordinateGrid(Parent(node)) is not { } grid) return;
        var matrix = EffectiveLocalMatrix(node);
        var rect = node.Kind == ObjectKind.Image ? SceneRenderer.ImagePlacementRect(node, grid) : OwnLayoutRect(node);
        var placed = System.Windows.Rect.Transform(rect, matrix);
        if (node.Kind == ObjectKind.Image) { node.Width = Math.Max(1, rect.Width); node.Height = Math.Max(1, rect.Height); }
        InitializeGridCoordinates(node, 1 + placed.Left / grid.CellSize, 1 + placed.Top / grid.CellHeight);
        node.LayoutRegion = "親の外側"; node.LayoutAnchor = "左上"; node.LayoutSizing = "固定";
        matrix.OffsetX = matrix.OffsetY = 0;
        matrix.Rotate(-node.Angle);
        node.PreservedTransform = matrix.IsIdentity ? null
            : new() { M11 = matrix.M11, M12 = matrix.M12, M21 = matrix.M21, M22 = matrix.M22 };
    }

    private void ValidateGridCoordinates(SceneObject node)
    {
        if (node.GridColumn.HasValue != node.GridRow.HasValue
            || node.GridColumn is { } column && (!double.IsFinite(column) || Math.Abs(column) > 100000)
            || node.GridRow is { } row && (!double.IsFinite(row) || Math.Abs(row) > 100000)
            || !double.IsFinite(node.PlacementOffsetX) || Math.Abs(node.PlacementOffsetX) > 100000
            || !double.IsFinite(node.PlacementOffsetY) || Math.Abs(node.PlacementOffsetY) > 100000)
            throw new InvalidDataException("マス座標またはずらし量が不正です。");
    }
}
