using System.IO;
using System.Text.Json;

namespace GridSketch;

public sealed class LibraryPart
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "部品";
    public SceneObject Template { get; set; } = new() { Kind = ObjectKind.Group };
    public List<PublishedPartProperty> Published { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore] public List<LibraryPart> Dependencies { get; set; } = [];

    public void Validate(IEnumerable<LibraryPart>? library = null)
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name) || Name.Length > 200 || Template is null || Template.Kind != ObjectKind.Group)
            throw new InvalidDataException("部品には名前（200文字以内）とグループまたはフレームが必要です。");
        if (Published is null || Published.Any(p => p is null || string.IsNullOrWhiteSpace(p.Label) || p.Label.Length > 200
            || !SceneDocument.CanOverridePartProperty(p.Property)) || Published.Count > 128)
            throw new InvalidDataException("公開プロパティの設定が不正です。");
        var check = new SceneDocument { Parts = (library ?? Dependencies.Append(this)).DistinctBy(p => p.Id).ToList() };
        check.Canvas.Children.Add(SceneDocument.CopyPartTemplate(Template)); check.ResolveParts(); check.Validate(false);
    }
    public static LibraryPart FromGroup(SceneObject group, string name, IEnumerable<LibraryPart>? library = null)
    {
        var part = new LibraryPart { Name = name, Template = SceneDocument.CopyPartTemplate(group) };
        part.Template.X = part.Template.Y = 0;
        part.Template.GridColumn = part.Template.GridRow = null;
        part.Template.UseParentLayout = false;
        part.Dependencies = SceneDocument.PartDependencies(SceneDocument.ReferencedParts([part.Template]), library ?? []);
        part.Validate(); return part;
    }
    public SceneObject CreateCopy()
    {
        Validate(); var copy = Clone(Template); copy.Name = Name; return copy;
    }
    internal static SceneObject Clone(SceneObject source)
    {
        var clone = JsonSerializer.Deserialize<SceneObject>(JsonSerializer.Serialize(source, SceneDocument.JsonOptions), SceneDocument.JsonOptions)!;
        var nodes = new List<SceneObject>();
        void Walk(SceneObject n) { nodes.Add(n); foreach (var child in n.Children) Walk(child); }
        Walk(clone);
        var ids = nodes.ToDictionary(n => n.Id, _ => Guid.NewGuid().ToString("N"));
        foreach (var node in nodes)
        {
            node.Id = ids[node.Id];
            var states = node.States.ToDictionary(s => s.Id, _ => Guid.NewGuid().ToString("N"));
            foreach (var state in node.States)
            {
                state.Id = states[state.Id];
                if (state.ObjectId is not null) state.ObjectId = ids[state.ObjectId];
            }
            foreach (var edge in node.StateEdges) edge.StateId = states[edge.StateId];
            foreach (var cell in node.StateCells) cell.StateId = states[cell.StateId];
        }
        return clone;
    }
}

public sealed class PartFile
{
    public string Format { get; set; } = "GridSketch.Part";
    public int Version { get; set; } = 1;
    public LibraryPart? Part { get; set; }
    public List<LibraryPart> Definitions { get; set; } = [];
    public static LibraryPart Import(string path)
    {
        if (new FileInfo(path).Length > 100_000_000) throw new InvalidDataException("部品ファイルは100MB以下にしてください。");
        var file = JsonSerializer.Deserialize<PartFile>(File.ReadAllText(path), SceneDocument.JsonOptions);
        if (file is null || file.Format != "GridSketch.Part" || file.Version is not (1 or 2) || file.Part is null) throw new InvalidDataException("未対応の部品ファイルです。");
        file.Part.Dependencies = file.Definitions.Where(p => p.Id != file.Part.Id).ToList();
        file.Part.Validate(); return file.Part;
    }
    public static void Export(LibraryPart part, string path, IEnumerable<LibraryPart>? library = null)
    {
        var definitions = SceneDocument.PartDependencies([part.Id], library ?? part.Dependencies.Append(part));
        part.Validate(definitions); var bytes = JsonSerializer.SerializeToUtf8Bytes(new PartFile { Version = 2, Part = part, Definitions = definitions.Where(p => p.Id != part.Id).ToList() }, SceneDocument.JsonOptions);
        if (bytes.Length > 100_000_000) throw new InvalidDataException("部品ファイルは100MB以下にしてください。");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed partial class SceneDocument
{
    public List<LibraryPart> Parts { get; set; } = [];
    public SceneObject InsertPart(LibraryPart part, SceneObject parent)
    {
        if (!All().Contains(parent) || !parent.IsContainer || !Editable(parent) || !PartStructureEditable(parent)) throw new InvalidOperationException("編集可能な追加先を選択してください。");
        bool added = Parts.All(p => p.Id != part.Id);
        if (added) Parts.Add(part);
        var copy = new SceneObject { Kind = ObjectKind.Group, Name = part.Name, HasLayoutFrame = true,
            Width = part.Template.Width, Height = part.Template.Height, Part = new() { DefinitionId = part.Id } };
        parent.Children.Insert(0, copy);
        if (parent.IsStateSelector && parent.UseObjectLayout)
        {
            copy.UseParentLayout = true; copy.LayoutRegion = "親の内側"; copy.LayoutSizing = "固定"; copy.LayoutAnchor = "中央";
        }
        int stateCount = parent.States.Count;
        try
        {
            ResolveParts();
            foreach (var child in Walk(copy)) AdoptGridCoordinates(child);
            if (parent.IsStateSelector) AssignNewState(parent, copy);
            Validate(); return copy;
        }
        catch
        {
            parent.Children.Remove(copy);
            if (added) Parts.Remove(part);
            if (parent.States.Count > stateCount) parent.States.RemoveRange(stateCount, parent.States.Count - stateCount);
            throw;
        }
    }
}
