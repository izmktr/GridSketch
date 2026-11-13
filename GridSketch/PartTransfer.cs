using System.IO;
using System.Text.Json;

namespace GridSketch;

public enum PartConflictPolicy { Separate, UseDestination, UpdateDestination }

public sealed partial class SceneDocument
{
    public static IEnumerable<string> ReferencedParts(IEnumerable<SceneObject> roots)
    {
        foreach (var node in roots)
        {
            if (node.Part is { } reference) yield return reference.DefinitionId;
            else foreach (var id in ReferencedParts(node.Children)) yield return id;
        }
    }
    public static List<LibraryPart> PartDependencies(IEnumerable<string> roots, IEnumerable<LibraryPart> library)
    {
        var definitions = library.ToDictionary(p => p.Id); var result = new List<LibraryPart>(); var visited = new HashSet<string>();
        void Visit(string id, HashSet<string> chain)
        {
            if (!chain.Add(id)) throw new InvalidDataException("パーツの循環参照はできません。");
            if (!definitions.TryGetValue(id, out var part)) throw new InvalidDataException("参照先のパーツ定義がありません。");
            if (visited.Add(id))
            {
                foreach (var nested in ReferencedParts([part.Template])) Visit(nested, chain);
                result.Add(part);
            }
            chain.Remove(id);
        }
        foreach (var id in roots) Visit(id, []);
        return result;
    }
    private static string PartFingerprint(string id, IEnumerable<LibraryPart> library)
        => JsonSerializer.Serialize(PartDependencies([id], library).OrderBy(p => p.Id), JsonOptions);
    public List<string> PartConflicts(IEnumerable<string> ids, IEnumerable<LibraryPart> source)
    {
        var sourceParts = source.ToList();
        return PartDependencies(ids, sourceParts).Where(p => Parts.Any(q => q.Id == p.Id)
            && PartFingerprint(p.Id, Parts) != PartFingerprint(p.Id, sourceParts)).Select(p => p.Name).ToList();
    }
    public Dictionary<string, string> ImportPartDefinitions(IEnumerable<string> ids, IEnumerable<LibraryPart> source, PartConflictPolicy policy = PartConflictPolicy.Separate)
    {
        var sourceParts = source.ToList(); var incoming = PartDependencies(ids, sourceParts);
        var mapping = new Dictionary<string, string>();
        foreach (var part in incoming)
        {
            var existing = Parts.FirstOrDefault(p => p.Id == part.Id);
            bool same = existing is not null && PartFingerprint(part.Id, Parts) == PartFingerprint(part.Id, sourceParts);
            if (existing is not null && (same || policy == PartConflictPolicy.UseDestination)) { mapping[part.Id] = existing.Id; continue; }
            var copy = JsonSerializer.Deserialize<LibraryPart>(JsonSerializer.Serialize(part, JsonOptions), JsonOptions)!;
            if (existing is not null && policy == PartConflictPolicy.Separate) copy.Id = Guid.NewGuid().ToString("N");
            mapping[part.Id] = copy.Id;
            RemapPartReferences([copy.Template], mapping);
            if (existing is not null && policy == PartConflictPolicy.UpdateDestination)
            { existing.Name = copy.Name; existing.Template = copy.Template; existing.Published = copy.Published; }
            else Parts.Add(copy);
        }
        return mapping;
    }
    public static void RemapPartReferences(IEnumerable<SceneObject> roots, IReadOnlyDictionary<string, string> mapping)
    {
        foreach (var node in roots)
        {
            if (node.Part is { } reference && mapping.TryGetValue(reference.DefinitionId, out var id)) reference.DefinitionId = id;
            if (!node.IsPart) RemapPartReferences(node.Children, mapping);
        }
    }
}
