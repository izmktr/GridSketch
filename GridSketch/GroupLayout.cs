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
        if (groups.Any(g => !g.OrganizeOnly))
            throw new InvalidOperationException("罫線やグリッド文字はフレームに入れられません。先に「グループに変換」を行ってください。");
    }
    public void PrepareGroupLayouts()
    {
        foreach (var node in All().Where(n => n.IsGridContent)) PrepareGridGroups(Parent(node)!);
    }
    public void EnableGroupTransform(SceneObject group) => ConvertContainer(group, false);
}
