using System.IO;

namespace GridSketch;

public sealed class CellState
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "状態";
    public string? ObjectId { get; set; }
}

public sealed class CellStatePlacement
{
    public int Column { get; set; }
    public int Row { get; set; }
    public string StateId { get; set; } = "";
}

public sealed partial class SceneDocument
{
    public static void AssignNewState(SceneObject selector, SceneObject child)
    {
        if (!selector.States.Any(s => s.ObjectId == child.Id))
            selector.States.Add(new CellState { Name = child.Name, ObjectId = child.Id });
    }
    public void ClearMissingStateObjects()
    {
        foreach (var selector in All().Where(n => n.IsStateSelector))
            foreach (var state in selector.States)
                if (state.ObjectId is not null && !selector.Children.Any(c => c.Id == state.ObjectId)) state.ObjectId = null;
    }
    public static string? NextCellState(SceneObject selector, CellPosition cell)
    {
        var current = selector.StateCells.Find(c => c.Column == cell.Column && c.Row == cell.Row)?.StateId;
        int index = current is null ? -1 : selector.States.FindIndex(s => s.Id == current);
        return index + 1 < selector.States.Count ? selector.States[index + 1].Id : null;
    }
    public static void SetCellState(SceneObject selector, CellPosition cell, string? stateId)
    {
        var existing = selector.StateCells.Find(c => c.Column == cell.Column && c.Row == cell.Row);
        if (stateId is null) { if (existing is not null) selector.StateCells.Remove(existing); }
        else if (existing is not null) existing.StateId = stateId;
        else selector.StateCells.Add(new() { Column = cell.Column, Row = cell.Row, StateId = stateId });
    }
    public SceneObject? SelectionOwner(SceneObject node)
    {
        for (var p = Parent(node); p is not null; p = Parent(p))
            if (p.IsStateSelector) return p;
        return null;
    }
    private void ValidateCellSelection(SceneObject node, SceneObject? parent)
    {
        if (node.TemplateRegion is not ("マス全体" or "余白の内側") || node.TemplateSizing is not ("固定サイズ" or "収める" or "親いっぱい")
            || node.TemplateAnchor is not ("中央" or "左上" or "上" or "右上" or "左" or "右" or "左下" or "下" or "右下"))
            throw new InvalidDataException("マス内の配置設定が不正です。");
        if (node.EdgeFilter is not ("すべて" or "水平辺のみ" or "垂直辺のみ") || node.VerticalRotation is not ("回転なし" or "右90°" or "左90°")) throw new InvalidDataException("辺切り替えの設定が不正です。");
        if (node.States is null || node.StateCells is null || node.StateEdges is null) throw new InvalidDataException("マス切り替えの設定が不正です。");
        if (SelectionOwner(node) is not null && node.Kind is not (ObjectKind.Group or ObjectKind.Image or ObjectKind.Text or ObjectKind.Shape or ObjectKind.Line))
            throw new InvalidDataException("切り替えオブジェクトの見本には画像・文字・図形・ライン・グループを配置できます。");
        if (!node.IsStateSelector) return;
        if (node.States.Count > 128 || node.States.Any(s => s is null || string.IsNullOrWhiteSpace(s.Id) || s.Name is null || s.Name.Length > 200)
            || node.States.Select(s => s.Id).Distinct().Count() != node.States.Count)
            throw new InvalidDataException("状態は128件以内で、重複しないIDと200文字以内の名前が必要です。");
        if (node.States.Any(s => s.ObjectId is not null && !node.Children.Any(c => c.Id == s.ObjectId)))
            throw new InvalidDataException("表示オブジェクトは切り替えオブジェクトの直下の子から指定してください。");
        if (node.Kind == ObjectKind.EdgeSelection)
        {
            if (node.StateEdges.Any(e => e is null || !e.Edge.IsInside(parent!.Columns, parent.Rows) || !node.States.Any(s => s.Id == e.StateId))
                || node.StateEdges.Select(e => e.Edge).Distinct().Count() != node.StateEdges.Count)
                throw new InvalidDataException("辺切り替えの配置位置または状態が不正です。");
            return;
        }
        if (node.StateCells.Any(c => c is null || c.Column < 0 || c.Row < 0 || c.Column >= parent!.Columns || c.Row >= parent.Rows || !node.States.Any(s => s.Id == c.StateId))
            || node.StateCells.Select(c => (c.Column, c.Row)).Distinct().Count() != node.StateCells.Count)
            throw new InvalidDataException("マス切り替えの配置位置または状態が不正です。");
    }
}
