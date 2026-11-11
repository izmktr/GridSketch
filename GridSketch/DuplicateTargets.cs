using System.IO;

namespace GridSketch;

public sealed partial class SceneDocument
{
    public IReadOnlyList<SceneObject> DuplicateTargets(IReadOnlyList<SceneObject> nodes, SceneDocument? sourceDocument = null)
    {
        sourceDocument ??= this;
        if (sourceDocument.ReparentSelectionError(nodes) is not null) return [];
        var snapshot = Snapshot();
        var targets = new List<SceneObject>();
        foreach (var target in All().Where(n => n.IsContainer && Editable(n)))
        {
            var trial = Restore(snapshot);
            try
            {
                trial.DuplicateIntoCore(nodes, trial.Find(target.Id)!, sourceDocument, sourceDocument == this);
                targets.Add(target);
            }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or ArgumentException or OverflowException) { }
        }
        return targets;
    }

    public IReadOnlyList<SceneObject> DuplicateInto(IReadOnlyList<SceneObject> nodes, SceneObject target, SceneDocument? sourceDocument = null)
    {
        sourceDocument ??= this;
        if (sourceDocument.ReparentSelectionError(nodes) is { } error) throw new InvalidOperationException(error);
        if (Find(target.Id) != target || !target.IsContainer || !Editable(target)) throw new InvalidOperationException("この親には複製できません。");
        var trial = Restore(Snapshot());
        trial.DuplicateIntoCore(nodes, trial.Find(target.Id)!, sourceDocument, sourceDocument == this);
        return DuplicateIntoCore(nodes, target, sourceDocument, sourceDocument == this);
    }

    private List<SceneObject> DuplicateIntoCore(IReadOnlyList<SceneObject> nodes, SceneObject target, SceneDocument sourceDocument, bool sameDocument)
    {
        var parent = sourceDocument.Parent(nodes[0])!;
        // Clone all sources before inserting anything, even if the target is inside a source.
        var copies = parent.Children.Where(nodes.Contains).Select(LibraryPart.Clone).ToList();
        foreach (var copy in copies)
        {
            if (sameDocument && RequiresGrid(copy) && sourceDocument.GridContainer(parent)?.Id != GridContainer(target)?.Id)
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
            // Preserve source coordinates relative to the parent, including cell template offsets.
            target.Children.Add(copy);
            foreach (var child in Walk(copy)) AdoptGridCoordinates(child);
            if (target.IsStateSelector) AssignNewState(target, copy);
        }
        target.LayerExpanded = true;
        PrepareGroupLayouts(); ClearMissingStateObjects(); Validate();
        return copies;
    }
}
