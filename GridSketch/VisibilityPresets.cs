using System.IO;

namespace GridSketch;

public sealed class VisibilityPreset
{
    public string Name { get; set; } = "";
    public HashSet<string> HiddenIds { get; set; } = [];
    public override string ToString() => Name;
}

public sealed partial class SceneDocument
{
    public List<VisibilityPreset> VisibilityPresets { get; set; } = [];

    public VisibilityPreset CaptureVisibility(string name) => new()
    {
        Name = name,
        HiddenIds = All().Where(n => n != Canvas && !n.Visible).Select(n => n.Id).ToHashSet()
    };

    public void ApplyVisibility(VisibilityPreset? preset)
    {
        foreach (var node in All()) node.Visible = node == Canvas || preset is null || !preset.HiddenIds.Contains(node.Id);
    }

    private void ValidateVisibilityPresets()
    {
        if (VisibilityPresets is null || VisibilityPresets.Count > 128 || VisibilityPresets.Any(p => p is null ||
            string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 200 || p.HiddenIds is null))
            throw new InvalidDataException("表示プリセットは128件以内、名前は1～200文字で指定してください。");
        var ids = All().Where(n => n != Canvas).Select(n => n.Id).ToHashSet();
        foreach (var preset in VisibilityPresets) preset.HiddenIds.IntersectWith(ids);
    }
}
