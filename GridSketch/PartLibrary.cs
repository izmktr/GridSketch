using System.IO;
using System.Text.Json;

namespace GridSketch;

public sealed class LibraryPart
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "部品";
    public SceneObject Template { get; set; } = new() { Kind = ObjectKind.Group };

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name) || Name.Length > 200 || Template is null || Template.Kind != ObjectKind.Group)
            throw new InvalidDataException("部品には名前（200文字以内）とグループが必要です。");
        var check = new SceneDocument(); check.Canvas.Children.Add(Template); check.Validate();
    }
    public static LibraryPart FromGroup(SceneObject group, string name)
    {
        var part = new LibraryPart { Name = name, Template = Clone(group) };
        part.Template.X = part.Template.Y = 0;
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
    public static LibraryPart Import(string path)
    {
        if (new FileInfo(path).Length > 100_000_000) throw new InvalidDataException("部品ファイルは100MB以下にしてください。");
        var file = JsonSerializer.Deserialize<PartFile>(File.ReadAllText(path), SceneDocument.JsonOptions);
        if (file is null || file.Format != "GridSketch.Part" || file.Version != 1 || file.Part is null) throw new InvalidDataException("未対応の部品ファイルです。");
        file.Part.Validate();
        // Import is always an independent entry, even when importing the same file twice.
        return LibraryPart.FromGroup(file.Part.Template, file.Part.Name);
    }
    public static void Export(LibraryPart part, string path)
    {
        part.Validate(); var bytes = JsonSerializer.SerializeToUtf8Bytes(new PartFile { Part = part }, SceneDocument.JsonOptions);
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
        if (!All().Contains(parent) || !parent.IsContainer || !Editable(parent)) throw new InvalidOperationException("編集可能な追加先を選択してください。");
        var copy = part.CreateCopy(); parent.Children.Insert(0, copy);
        int stateCount = parent.States.Count;
        try
        {
            if (parent.Kind == ObjectKind.CellSelection) AssignNewState(parent, copy);
            Validate(); return copy;
        }
        catch
        {
            parent.Children.Remove(copy);
            if (parent.States.Count > stateCount) parent.States.RemoveRange(stateCount, parent.States.Count - stateCount);
            throw;
        }
    }
}
