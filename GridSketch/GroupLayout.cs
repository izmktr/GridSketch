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
            if (group.X != 0 || group.Y != 0 || group.Angle != 0)
                throw new InvalidOperationException($"「{group.Name}」には移動・回転が設定されているため、整理用に変更できません。位置X/Yと回転を0に戻してください。");
            if (!group.OrganizeOnly && !Editable(group)) throw new InvalidOperationException("ロックされたグループを整理用に変更できません。");
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
