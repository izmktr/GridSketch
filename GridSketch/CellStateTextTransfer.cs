using System.Globalization;

namespace GridSketch;

public static class CellStateTextTransfer
{
    public static string Export(SceneObject node, SceneObject grid, GridTextFormat format)
    {
        if (node.Kind != ObjectKind.CellSelection) throw new InvalidOperationException("マス切り替えを選択してください。");
        var numbers = node.States.Select((s, i) => (s.Id, Number: i + 1)).ToDictionary(s => s.Id, s => s.Number);
        var placed = node.StateCells.ToDictionary(c => (c.Column, c.Row), c => numbers[c.StateId]);
        var text = new SceneObject { Kind = ObjectKind.GridText };
        for (int r = 0; r < grid.Rows; r++)
            for (int c = 0; c < grid.Columns; c++)
                text.Glyphs.Add(new() { Column = c, Row = r, Text = placed.GetValueOrDefault((c, r)).ToString(CultureInfo.InvariantCulture) });
        return GridTextTransfer.Export(text, grid, format);
    }

    public static List<CellStatePlacement> Parse(SceneObject node, SceneObject grid, string text, GridTextFormat format)
    {
        var cells = GridTextTransfer.Parse(text, format, grid.Columns, grid.Rows);
        var result = new List<CellStatePlacement>();
        for (int r = 0; r < grid.Rows; r++)
            for (int c = 0; c < grid.Columns; c++)
            {
                var value = cells[r][c];
                if (value.Length == 0 || value.Any(ch => ch < '0' || ch > '9')
                    || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) || number > node.States.Count)
                    throw new FormatException($"{r + 1}行{c + 1}列は0～{node.States.Count}の状態番号で指定してください。なしは0です。");
                if (number > 0) result.Add(new() { Column = c, Row = r, StateId = node.States[number - 1].Id });
            }
        return result;
    }
}

public sealed partial class SceneDocument
{
    public void ImportCellStates(SceneObject node, string text, GridTextFormat format)
    {
        if (Find(node.Id) != node || node.Kind != ObjectKind.CellSelection || GridFor(node) is not { } grid) throw new InvalidOperationException("対象のマス切り替えがありません。");
        if (!Editable(node) || !Shown(node)) throw new InvalidOperationException("非表示またはロック中のマス切り替えには反映できません。");
        node.StateCells = CellStateTextTransfer.Parse(node, grid, text, format);
    }
}
