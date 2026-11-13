using System.Windows;
using System.Windows.Media;

namespace GridSketch;

public sealed partial class SceneDocument
{
    private static IEnumerable<SceneObject> PlacementRoots(IEnumerable<SceneObject> nodes)
        => nodes.SelectMany(n => n.IsOrganizer ? PlacementRoots(n.Children) : new[] { n });

    private string? ContainerOperationError(Action<SceneDocument> action)
    {
        try { var trial = Restore(Snapshot()); action(trial); trial.Validate(); return null; }
        catch (Exception e) when (e is InvalidOperationException or System.IO.InvalidDataException or ArgumentException) { return e.Message; }
    }
    public string? ConversionError(SceneObject node, bool organizer)
        => ContainerOperationError(d => d.ConvertContainerCore(d.Find(node.Id)!, organizer));
    public string? UnwrapError(SceneObject node)
        => ContainerOperationError(d => d.UnwrapCore(d.Find(node.Id)!));
    public string? FrameWrapError(IReadOnlyList<SceneObject> nodes)
        => ContainerOperationError(d => d.WrapCore(nodes.Select(n => d.Find(n.Id)!).ToList(), true));

    private void RequireContainerEditable(SceneObject node)
    {
        if (node.Kind != ObjectKind.Group || Parent(node) is null) throw new InvalidOperationException("グループまたはフレームを選択してください。");
        if (Walk(node).Any(n => !Editable(n))) throw new InvalidOperationException("ロックされたオブジェクトを含むため操作できません。");
    }

    // Store placement in visible, editable properties only. Shear and unsupported
    // scaling must never become an invisible compensation matrix.
    private void SetEditablePlacement(SceneObject node, Matrix local)
    {
        if (node.Kind == ObjectKind.Line) { NormalizeLine(node, local, false); return; }
        double sx = Math.Sqrt(local.M11 * local.M11 + local.M12 * local.M12);
        double sy = Math.Sqrt(local.M21 * local.M21 + local.M22 * local.M22);
        if (local.Determinant <= 0 || Math.Abs(local.M11 * local.M21 + local.M12 * local.M22) > 1e-7 * sx * sy)
            throw new InvalidOperationException("傾斜や反転を通常のプロパティで表せないため、見た目を保って操作できません。");
        bool scaled = Math.Abs(sx - 1) > 1e-7 || Math.Abs(sy - 1) > 1e-7;
        if (scaled)
        {
            if (node.Kind == ObjectKind.Image) { node.Width *= sx; node.Height *= sy; }
            else if (Math.Abs(sx - sy) < 1e-7 && (node.Kind is ObjectKind.Text or ObjectKind.Line || node.Kind == ObjectKind.Shape && node.Shape != "角丸"))
            {
                node.Width *= sx; node.Height *= sx; node.Thickness *= sx;
                if (node.Kind == ObjectKind.Text) { node.FontSize *= sx; node.Style.OutlineWidth *= sx; }
                if (node.Kind == ObjectKind.Line)
                {
                    node.EndX *= sx; node.EndY *= sx; node.ControlX *= sx; node.ControlY *= sx; node.ArrowSize *= sx;
                }
            }
            else throw new InvalidOperationException($"「{node.Name}」の拡縮を見た目を保ってプロパティに戻せません。サイズの扱いを原寸にしてから操作してください。");
        }
        node.PreservedTransform = null;
        node.SnapToCell = false;
        node.GridColumn = node.GridRow = null;
        node.PlacementOffsetX = node.PlacementOffsetY = 0;
        node.UseParentLayout = false; node.LayoutSizing = "固定";
        node.Angle = Math.Atan2(local.M12, local.M11) * 180 / Math.PI;
        node.X = node.Y = 0;
        var rotation = LocalMatrix(node);
        node.X = local.OffsetX - rotation.OffsetX; node.Y = local.OffsetY - rotation.OffsetY;
        AdoptGridCoordinates(node);
        // AdoptGridCoordinates can leave a floating-point identity after rotation.
        node.PreservedTransform = null;
        EnableParentLayout(node);
    }

    private void RestorePlacements(Dictionary<SceneObject, Matrix> placements)
    {
        foreach (var (node, world) in placements)
        {
            var inverse = World(Parent(node)!); inverse.Invert();
            var local = world; local.Append(inverse);
            SetEditablePlacement(node, local);
        }
    }
    private void ReparentOrganizer(SceneObject node, SceneObject target)
    {
        RequireContainerEditable(node);
        if (!Editable(target)) throw new InvalidOperationException("ロックされています。");
        var roots = PlacementRoots(node.Children).ToList();
        var gridLines = roots.Where(n => n.Kind == ObjectKind.Line && n.PointLine && UsesGridCoordinates(n)).ToList();
        var placements = roots.Where(n => !(UsesGridCoordinates(n) && CoordinateGrid(target) is not null))
            .ToDictionary(n => n, World);
        Parent(node)!.Children.Remove(node); target.Children.Insert(0, node);
        if (CoordinateGrid(target) is not null)
            foreach (var line in gridLines) AdoptGridCoordinates(line);
        if (target.IsStateSelector) AssignNewState(target, node);
        RestorePlacements(placements);
    }
    private Rect ContentsRect(IEnumerable<SceneObject> roots, SceneObject parent)
    {
        var inverse = World(parent); inverse.Invert();
        var bounds = Rect.Empty;
        foreach (var root in roots)
        {
            var matrix = World(root); matrix.Append(inverse);
            bounds.Union(new MatrixTransform(matrix).TransformBounds(OwnLayoutRect(root)));
        }
        return bounds.IsEmpty ? new Rect(0, 0, 100, 100) : bounds;
    }
    private void SetFrame(SceneObject node, Rect bounds)
    {
        node.OrganizeOnly = false; node.HasLayoutFrame = true;
        node.LayoutHasInner = false; node.LayoutInsetX = node.LayoutInsetY = 0;
        node.Width = Math.Max(1, bounds.Width); node.Height = Math.Max(1, bounds.Height);
        SetEditablePlacement(node, new Matrix(1, 0, 0, 1, bounds.X, bounds.Y));
    }
    public void ConvertContainer(SceneObject node, bool organizer)
    {
        if (ConversionError(node, organizer) is { } error) throw new InvalidOperationException(error);
        ConvertContainerCore(node, organizer);
    }
    private void ConvertContainerCore(SceneObject node, bool organizer)
    {
        RequireContainerEditable(node);
        if (node.OrganizeOnly == organizer) return;
        if (SelectionOwner(node) is { UseObjectLayout: false }) throw new InvalidOperationException("従来方式の切り替え見本は見た目を保って変換できません。");
        if (!organizer && RequiresGrid(node)) throw new InvalidOperationException("グリッド依存の子を含むため、フレームに変換できません。");
        var roots = PlacementRoots(node.Children).ToList();
        var placements = roots.ToDictionary(n => n, World);
        var bounds = ContentsRect(roots, Parent(node)!);
        if (organizer)
        {
            node.OrganizeOnly = true; node.HasLayoutFrame = false;
            node.LayoutHasInner = false; node.LayoutInsetX = node.LayoutInsetY = 0;
            node.X = node.Y = node.Angle = 0; node.GridColumn = node.GridRow = null;
            node.PreservedTransform = null; node.UseParentLayout = false;
            node.PlacementOffsetX = node.PlacementOffsetY = 0;
        }
        else SetFrame(node, bounds);
        RestorePlacements(placements);
    }
    public SceneObject WrapContainers(IReadOnlyList<SceneObject> nodes, bool frame)
    {
        if (ContainerOperationError(d => d.WrapCore(nodes.Select(n => d.Find(n.Id)!).ToList(), frame)) is { } error)
            throw new InvalidOperationException(error);
        return WrapCore(nodes, frame);
    }
    private SceneObject WrapCore(IReadOnlyList<SceneObject> nodes, bool frame)
    {
        if (nodes.Count == 0 || Parent(nodes[0]) is not { } parent || nodes.Any(n => Parent(n) != parent || !Editable(n)))
            throw new InvalidOperationException("同じ親の編集可能なオブジェクトを選択してください。");
        var ordered = parent.Children.Where(nodes.Contains).ToList();
        int index = parent.Children.IndexOf(ordered[0]);
        if (!parent.Children.Skip(index).Take(ordered.Count).SequenceEqual(ordered) || parent.IsStateSelector && nodes.Count != 1)
            throw new InvalidOperationException("連続したオブジェクトを選択してください。");
        if (frame && nodes.Any(RequiresGrid)) throw new InvalidOperationException("グリッド依存のオブジェクトはフレームにまとめられません。");
        if (frame && SelectionOwner(nodes[0]) is { UseObjectLayout: false }) throw new InvalidOperationException("従来方式の切り替え見本はフレームにまとめられません。");
        if (frame && nodes.SelectMany(Walk).Any(n => !Editable(n))) throw new InvalidOperationException("ロックされた子を含むため操作できません。");
        var roots = PlacementRoots(ordered).ToList();
        var placements = roots.ToDictionary(n => n, World);
        var bounds = ContentsRect(roots, parent);
        var container = new SceneObject { Kind = ObjectKind.Group, Name = frame ? "フレーム" : "グループ", OrganizeOnly = !frame };
        parent.Children.Insert(index, container);
        if (frame) SetFrame(container, bounds);
        foreach (var node in ordered) parent.Children.Remove(node);
        container.Children = ordered;
        if (parent.IsStateSelector)
            foreach (var state in parent.States.Where(s => s.ObjectId == ordered[0].Id)) state.ObjectId = container.Id;
        if (frame) RestorePlacements(placements);
        return container;
    }
    public void UnwrapContainer(SceneObject node)
    {
        if (UnwrapError(node) is { } error) throw new InvalidOperationException(error);
        UnwrapCore(node);
    }
    private void UnwrapCore(SceneObject node)
    {
        RequireContainerEditable(node);
        var parent = Parent(node)!;
        if (parent.IsStateSelector) throw new InvalidOperationException("切り替えの見本は解除できません。子を個別に移動して割り当て直してください。");
        if (node.IsFrame && SelectionOwner(node) is { UseObjectLayout: false }) throw new InvalidOperationException("従来方式の切り替え見本は見た目を保ってフレーム解除できません。");
        var placements = PlacementRoots(node.Children).ToDictionary(n => n, World);
        int index = parent.Children.IndexOf(node);
        parent.Children.Remove(node); parent.Children.InsertRange(index, node.Children); node.Children.Clear();
        if (!node.OrganizeOnly) RestorePlacements(placements);
    }
}
