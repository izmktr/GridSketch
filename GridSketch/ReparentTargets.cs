using System.IO;

namespace GridSketch;

public sealed partial class SceneDocument
{
    public IReadOnlyList<SceneObject> ReparentTargets(SceneObject node)
    {
        if (node == Canvas || Find(node.Id) != node || !Editable(node)) return [];
        var excluded = Walk(node).Select(n => n.Id).ToHashSet();
        var parent = Parent(node);
        string snapshot = Snapshot();
        var targets = new List<SceneObject>();
        foreach (var candidate in All().Where(n => n.IsContainer && n != parent && !excluded.Contains(n.Id) && Editable(n)))
        {
            // Use the same mutation and validation as confirmation, on an isolated copy.
            // This includes automatic group conversion, state limits and layout constraints.
            var trial = Restore(snapshot);
            try
            {
                trial.Reparent(trial.Find(node.Id)!, trial.Find(candidate.Id)!);
                trial.PrepareGroupLayouts(); trial.ClearMissingStateObjects(); trial.Validate();
                targets.Add(candidate);
            }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or ArgumentException or OverflowException) { }
        }
        return targets;
    }
}
