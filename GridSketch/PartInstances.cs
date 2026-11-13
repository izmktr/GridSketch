using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GridSketch;

public sealed class PartReference
{
    public string DefinitionId { get; set; } = "";
    public Dictionary<string, Dictionary<string, JsonElement>> Overrides { get; set; } = [];
}
public sealed class PublishedPartProperty
{
    public string Path { get; set; } = "";
    public string Property { get; set; } = "Text";
    public string Label { get; set; } = "文章";
}

public sealed partial class SceneDocument
{
    private static readonly PropertyInfo[] PartValueProperties = typeof(SceneObject).GetProperties()
        .Where(p => p.CanWrite && p.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition != JsonIgnoreCondition.Always
            && p.Name is not ("Id" or "Kind" or "StoredChildren" or "Part")).ToArray();
    public static bool CanOverridePartProperty(string property)
        => property is not null && (property.StartsWith("Style.") ? typeof(LayerStyle).GetProperty(property[6..]) is { CanWrite: true }
            : PartValueProperties.Any(p => p.Name == property && p.Name != "Style"));
    public static JsonElement PartValue(SceneObject node, string property)
    {
        object owner = property.StartsWith("Style.") ? node.Style : node;
        var info = owner.GetType().GetProperty(property.StartsWith("Style.") ? property[6..] : property)
            ?? throw new InvalidOperationException("対象のプロパティがありません。");
        return JsonSerializer.SerializeToElement(info.GetValue(owner), info.PropertyType, JsonOptions);
    }
    private static void WritePartValue(SceneObject node, string property, JsonElement value)
    {
        if (!CanOverridePartProperty(property)) throw new InvalidDataException("上書きできないプロパティです。");
        object owner = property.StartsWith("Style.") ? node.Style : node;
        var info = owner.GetType().GetProperty(property.StartsWith("Style.") ? property[6..] : property)!;
        info.SetValue(owner, value.Deserialize(info.PropertyType, JsonOptions));
    }
    private static IEnumerable<string> PartPropertyNames => PartValueProperties.Where(p => p.Name != "Style").Select(p => p.Name)
        .Concat(typeof(LayerStyle).GetProperties().Where(p => p.CanWrite).Select(p => "Style." + p.Name));
    public SceneObject? PartOwner(SceneObject node) => node.PartOwnerId is { } id ? Find(id) : null;
    public static bool PartStructureEditable(SceneObject node) => !node.IsPart && node.PartOwnerId is null;
    public static SceneObject CopyPartTemplate(SceneObject node)
        => JsonSerializer.Deserialize<SceneObject>(JsonSerializer.Serialize(node, JsonOptions), JsonOptions)!;

    public void ResolveParts()
    {
        int count = 0;
        var definitions = Parts.ToDictionary(p => p.Id);
        void Resolve(SceneObject node, HashSet<string> chain)
        {
            if (node.Part is not { } reference) { foreach (var child in node.Children) Resolve(child, chain); return; }
            if (reference.Overrides is null || reference.Overrides.Any(p => p.Value is null)) throw new InvalidDataException("パーツの上書き設定が不正です。");
            if (!definitions.TryGetValue(reference.DefinitionId, out var definition)) throw new InvalidDataException("パーツの参照元がライブラリにありません。");
            if (!chain.Add(definition.Id)) throw new InvalidDataException("パーツの循環参照はできません。");
            var old = Walk(node).Skip(1).ToDictionary(n => n.Id);
            SceneObject Build(SceneObject copy, string prefix)
            {
                if (++count > 5000) throw new InvalidDataException("パーツの展開後のオブジェクト数が多すぎます。");
                string path = prefix + copy.Id;
                copy.Id = node.Id + ":" + path;
                copy.PartOwnerId = node.Id; copy.PartSourcePath = path;
                // Ordinary reparenting within a definition must not invalidate overrides.
                // Only nested reference boundaries add a path segment.
                copy.Children = copy.IsPart ? [] : copy.Children.Select(c => Build(c, prefix)).ToList();
                // Remap selector references using the same stable paths as its children.
                foreach (var state in copy.States)
                    if (state.ObjectId is { } objectId) state.ObjectId = node.Id + ":" + prefix + objectId;
                if (copy.IsPart)
                {
                    Resolve(copy, chain);
                    foreach (var child in Walk(copy).Skip(1))
                    {
                        string nestedPath = path + "/" + child.PartSourcePath;
                        child.PartOwnerId = node.Id; child.PartSourcePath = nestedPath;
                        if (reference.Overrides.TryGetValue(nestedPath, out var nestedValues))
                            foreach (var (key, value) in nestedValues) WritePartValue(child, key, value);
                    }
                }
                if (reference.Overrides.TryGetValue(path, out var values))
                    foreach (var (key, value) in values) WritePartValue(copy, key, value);
                return copy;
            }
            var desired = Build(CopyPartTemplate(definition.Template), "");
            var paths = Walk(desired).ToDictionary(n => n.PartSourcePath!);
            foreach (var child in Walk(desired))
                foreach (var state in child.States)
                    if (state.ObjectId is { } id && id.StartsWith("@part:") && paths.TryGetValue(id[6..], out var target)) state.ObjectId = target.Id;
            SceneObject Reuse(SceneObject desiredNode)
            {
                var children = desiredNode.Children.Select(Reuse).ToList();
                if (!old.TryGetValue(desiredNode.Id, out var existing)) { desiredNode.Children = children; return desiredNode; }
                foreach (var property in PartValueProperties.Where(p => p.Name != "LayerExpanded")) property.SetValue(existing, property.GetValue(desiredNode));
                existing.Kind = desiredNode.Kind; existing.Part = desiredNode.Part;
                existing.PartOwnerId = desiredNode.PartOwnerId; existing.PartSourcePath = desiredNode.PartSourcePath;
                existing.Children = children; return existing;
            }
            node.Children = [Reuse(desired)];
            chain.Remove(definition.Id);
        }
        Resolve(Canvas, []);
    }

    public void CapturePartOverrides(SceneDocument before, bool force = false)
    {
        foreach (var instance in All().Where(n => n.IsPart && n.PartOwnerId is null).ToList())
        {
            var previous = before.Find(instance.Id);
            if (previous?.Part is null) continue;
            // A definition update is inheritance, not an instance edit (including structural edits).
            if (PartFingerprint(instance.Part!.DefinitionId, Parts) != PartFingerprint(previous.Part.DefinitionId, before.Parts)) continue;
            // Explicit override/reset operations already recorded their changes.
            if (!force && JsonSerializer.Serialize(previous.Part, JsonOptions) != JsonSerializer.Serialize(instance.Part, JsonOptions)) continue;
            var oldNodes = Walk(previous).Skip(1).ToDictionary(n => n.Id);
            var currentNodes = Walk(instance).Skip(1).ToList();
            if (!oldNodes.Keys.ToHashSet().SetEquals(currentNodes.Select(n => n.Id))
                || currentNodes.Any(n => !n.Children.Select(c => c.Id).SequenceEqual(oldNodes[n.Id].Children.Select(c => c.Id))))
                throw new InvalidOperationException("パーツの構造は定義側で編集してください。個別に変更する場合は独立化してください。");
            foreach (var node in currentNodes)
                foreach (var key in PartPropertyNames.Where(k => k != "LayerExpanded"))
                {
                    var value = PartValue(node, key);
                    if (value.GetRawText() == PartValue(oldNodes[node.Id], key).GetRawText())
                    {
                        if (force && instance.Part!.Overrides.TryGetValue(node.PartSourcePath!, out var current))
                        {
                            if (previous.Part.Overrides.TryGetValue(node.PartSourcePath!, out var original) && original.TryGetValue(key, out var originalValue)) current[key] = originalValue.Clone();
                            else current.Remove(key);
                            if (current.Count == 0) instance.Part.Overrides.Remove(node.PartSourcePath!);
                        }
                        continue;
                    }
                    SetPartOverride(instance, node.PartSourcePath!, key, value, resolve: false);
                }
        }
    }
    public void SetPartOverride(SceneObject instance, string path, string property, JsonElement value, bool resolve = true)
    {
        if (instance.Part is null || !CanOverridePartProperty(property)) throw new InvalidOperationException("パーツの上書き先が不正です。");
        if (property == "States")
        {
            var states = value.Deserialize<List<CellState>>(JsonOptions)!;
            foreach (var state in states)
                if (state.ObjectId is { } id && Find(id) is { PartSourcePath: { } targetPath } target && target.PartOwnerId == instance.Id)
                    state.ObjectId = "@part:" + targetPath;
            value = JsonSerializer.SerializeToElement(states, JsonOptions);
        }
        if (!instance.Part.Overrides.TryGetValue(path, out var values)) instance.Part.Overrides[path] = values = [];
        values[property] = value.Clone();
        if (resolve) ResolveParts();
    }
    public void ResetPartOverride(SceneObject instance, string? path = null, string? property = null)
    {
        if (instance.Part is not { } reference) return;
        if (path is null) reference.Overrides.Clear();
        else if (property is null) reference.Overrides.Remove(path);
        else if (reference.Overrides.TryGetValue(path, out var values))
        { values.Remove(property); if (values.Count == 0) reference.Overrides.Remove(path); }
        ResolveParts();
    }
    public void DetachPart(SceneObject instance)
    {
        if (!instance.IsPart || instance.PartOwnerId is not null || !Editable(instance)) throw new InvalidOperationException("編集可能なパーツを選択してください。");
        ResolveParts(); instance.Part = null;
        void Clear(SceneObject node)
        {
            node.PartOwnerId = node.PartSourcePath = null;
            node.Part = null;
            foreach (var child in node.Children) Clear(child);
        }
        foreach (var child in instance.Children) Clear(child);
        ResolveParts();
    }
    public bool PartInUse(string id) => All().Any(n => n.Part?.DefinitionId == id)
        || Parts.Any(p => p.Id != id && Walk(p.Template).Any(n => n.Part?.DefinitionId == id));
}
