using System.IO;

namespace GridSketch;

public sealed partial class SceneDocument
{
    public IReadOnlyList<SceneObject> DuplicateTargets(IReadOnlyList<SceneObject> nodes)
    {
        if (ReparentSelectionError(nodes) is not null) return [];
        var snapshot = Snapshot();
        var targets = new List<SceneObject>();
        foreach (var target in All().Where(n => n.IsContainer && Editable(n)))
        {
            var trial = Restore(snapshot);
            try
            {
                trial.DuplicateIntoCore(nodes.Select(n => trial.Find(n.Id)!).ToList(), trial.Find(target.Id)!);
                targets.Add(target);
            }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or ArgumentException or OverflowException) { }
        }
        return targets;
    }

    public IReadOnlyList<SceneObject> DuplicateInto(IReadOnlyList<SceneObject> nodes, SceneObject target)
    {
        if (ReparentSelectionError(nodes) is { } error) throw new InvalidOperationException(error);
        if (Find(target.Id) != target || !target.IsContainer || !Editable(target)) throw new InvalidOperationException("この親には複製できません。");
        var trial = Restore(Snapshot());
        trial.DuplicateIntoCore(nodes.Select(n => trial.Find(n.Id)!).ToList(), trial.Find(target.Id)!);
        return DuplicateIntoCore(nodes, target);
    }

    private List<SceneObject> DuplicateIntoCore(IReadOnlyList<SceneObject> nodes, SceneObject target)
    {
        var parent = Parent(nodes[0])!;
        // Clone all sources before inserting anything, even if the target is inside a source.
        var copies = parent.Children.Where(nodes.Contains).Select(LibraryPart.Clone).ToList();
        foreach (var copy in copies)
        {
            if (RequiresGrid(copy) && GridContainer(parent) != GridContainer(target))
                throw new InvalidOperationException("グリッド依存の内容は同じグリッド内に複製してください。");
            if (target != parent)
                copy.Name = System.Text.RegularExpressions.Regex.Replace(copy.Name, @"(?: のコピー(?: \d+)?)+$", "");
            if (target.Children.Any(n => n.Name == copy.Name))
            {
                var stem = copy.Name.Length > 180 ? copy.Name[..180] : copy.Name;
                copy.Name = stem + " のコピー";
                int suffix = 2;
                while (target.Children.Any(n => n.Name == copy.Name)) copy.Name = stem + " のコピー " + suffix++;
            }
            // Duplication keeps local coordinates and layout settings. Reparent would
            // rewrite them to preserve world coordinates, moving the copy within its new parent.
            target.Children.Add(copy);
            if (target.IsStateSelector) AssignNewState(target, copy);
        }
        target.LayerExpanded = true;
        PrepareGroupLayouts(); ClearMissingStateObjects(); Validate();
        return copies;
    }
}
