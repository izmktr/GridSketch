using System.Windows;

namespace GridSketch;

public sealed partial class SceneDocument
{
    public void SetImageCellPlacement(SceneObject node, bool enabled)
    {
        if (node.Kind != ObjectKind.Image || node.SnapToCell == enabled) return;
        if (!Editable(node)) throw new InvalidOperationException("ロックされています。");
        var grid = GridFor(node) ?? throw new InvalidOperationException("マス配置には親グリッドが必要です。");
        if (!enabled)
        {
            // A cell image is drawn at cell coordinates within its parent's coordinate space.
            var box = SceneRenderer.ImagePlacementRect(node, grid);
            node.X += box.X; node.Y += box.Y; node.Width = box.Width; node.Height = box.Height;
            node.SnapToCell = false; node.UseParentLayout = false;
        }
        else
        {
            var box = SceneRenderer.ImagePlacementRect(node, grid);
            var center = World(node).Transform(new Point(box.X + box.Width / 2, box.Y + box.Height / 2));
            var inverse = World(grid); inverse.Invert(); center = inverse.Transform(center);
            PrepareGridGroups(Parent(node)!);
            node.Column = Math.Clamp((int)Math.Floor(center.X / grid.CellSize), 0, grid.Columns - 1);
            node.Row = Math.Clamp((int)Math.Floor(center.Y / grid.CellHeight), 0, grid.Rows - 1);
            node.SnapToCell = true; node.UseParentLayout = false;
            node.X = node.Y = node.Angle = 0; node.PreservedTransform = null;
        }
    }
}
