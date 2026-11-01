namespace GridSketch;

public sealed partial class SceneDocument
{
    public static IEnumerable<SceneObject> GridMembers(SceneObject container)
    {
        foreach (var child in container.Children)
        {
            yield return child;
            if (child.Kind == ObjectKind.Group)
                foreach (var nested in GridMembers(child)) yield return nested;
        }
    }
    public static bool RequiresGrid(SceneObject node) => node.IsGridContent ||
        node.Kind == ObjectKind.Group && GridMembers(node).Any(n => n.IsGridContent);

    public SceneObject? GridContainer(SceneObject? node)
    {
        while (node is { Kind: ObjectKind.Group }) node = Parent(node);
        return node?.Kind == ObjectKind.Grid ? node : null;
    }
    public void PrepareGridGroups(SceneObject target)
    {
        var groups = new List<SceneObject>();
        SceneObject? current = target;
        while (current is { Kind: ObjectKind.Group }) { groups.Add(current); current = Parent(current); }
        if (current?.Kind != ObjectKind.Grid) throw new InvalidOperationException("罫線やグリッド文字は、所属グリッド内のグループに配置してください。");
        foreach (var group in groups)
        {
            if (group.X != 0 || group.Y != 0 || group.Angle != 0 || !EffectiveLocalMatrix(group).IsIdentity)
                throw new InvalidOperationException($"「{group.Name}」には移動・回転・拡縮が設定されているため、整理用に変更できません。位置と回転を0にし、親に合わせる配置や保持した拡縮を解除してください。");
            if (!group.OrganizeOnly && !Editable(group)) throw new InvalidOperationException("ロックされたグループを整理用に変更できません。");
        }
        // Capture placements before any ancestor starts passing through a different frame.
        var placements = new Dictionary<SceneObject, System.Windows.Media.Matrix>();
        void Capture(SceneObject container)
        {
            foreach (var child in container.Children)
            {
                if (child.Kind == ObjectKind.Group && child.OrganizeOnly) Capture(child);
                else if (ObjectLayoutActive(child)) placements[child] = EffectiveLocalMatrix(child);
            }
        }
        foreach (var group in groups.Where(g => !g.OrganizeOnly)) Capture(group);
        foreach (var (child, matrix) in placements)
        {
            child.UseParentLayout = false;
            child.Angle = 0;
            child.X = matrix.OffsetX; child.Y = matrix.OffsetY;
            child.PreservedTransform = new() { M11 = matrix.M11, M12 = matrix.M12, M21 = matrix.M21, M22 = matrix.M22 };
        }
        foreach (var group in groups) group.OrganizeOnly = true;
    }
    public void PrepareGroupLayouts()
    {
        foreach (var node in All().Where(n => n.IsGridContent)) PrepareGridGroups(Parent(node)!);
    }
    public void EnableGroupTransform(SceneObject group)
    {
        if (group.Kind != ObjectKind.Group || !Editable(group)) throw new InvalidOperationException("編集可能なグループを選択してください。");
        if (RequiresGrid(group)) throw new InvalidOperationException("罫線やグリッド文字などが含まれているため、一括変形用には戻せません。");
        group.OrganizeOnly = false;
    }
}
