using System.IO;

namespace GridSketch;

public sealed partial class SceneDocument
{
    public IReadOnlyList<SceneObject> ReparentTargets(SceneObject node)
        => ReparentTargets([node]);

    public string? ReparentSelectionError(IReadOnlyList<SceneObject> nodes)
    {
        if (nodes.Count == 0 || nodes.Any(n => n == Canvas || Find(n.Id) != n)) return "キャンバスは親を変更できません。";
        if (nodes.Any(n => !Editable(n))) return "ロックされたレイヤーが含まれています。";
        if (nodes.Any(n => Parent(n) != Parent(nodes[0]))) return "同じ親を持つレイヤーを選択してください。";
        return null;
    }

    public IReadOnlyList<SceneObject> ReparentTargets(IReadOnlyList<SceneObject> nodes)
    {
        if (ReparentSelectionError(nodes) is not null) return [];
        var excluded = nodes.SelectMany(Walk).Select(n => n.Id).ToHashSet();
        var parent = Parent(nodes[0]);
        string snapshot = Snapshot();
        var targets = new List<SceneObject>();
        foreach (var candidate in All().Where(n => n.IsContainer && n != parent && !excluded.Contains(n.Id) && Editable(n)))
        {
            // Use the same mutation and validation as confirmation, on an isolated copy.
            // This includes automatic group conversion, state limits and layout constraints.
            var trial = Restore(snapshot);
            try
            {
                trial.ReparentBatchCore(nodes.Select(n => trial.Find(n.Id)!).ToList(), trial.Find(candidate.Id)!);
                targets.Add(candidate);
            }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or ArgumentException or OverflowException) { }
        }
        return targets;
    }

    public void ReparentMany(IReadOnlyList<SceneObject> nodes, SceneObject target)
    {
        if (ReparentSelectionError(nodes) is { } error) throw new InvalidOperationException(error);
        // Check the entire batch before touching the live document, including combined state limits.
        var trial = Restore(Snapshot());
        trial.ReparentBatchCore(nodes.Select(n => trial.Find(n.Id)!).ToList(), trial.Find(target.Id)!);
        ReparentBatchCore(nodes, target);
    }

    private void ReparentBatchCore(IReadOnlyList<SceneObject> nodes, SceneObject target)
    {
        var parent = Parent(nodes[0])!;
        if (target == parent) throw new InvalidOperationException("現在と異なる親を選択してください。");
        var ordered = parent.Children.Where(nodes.Contains).ToList();
        foreach (var node in ordered)
        {
            Reparent(node, target);
            target.Children.Remove(node); target.Children.Add(node);
        }
        target.LayerExpanded = true;
        PrepareGroupLayouts(); ClearMissingStateObjects(); Validate();
    }
}
