using System.Globalization;
using System.Text;

namespace GridSketch;

public enum GridTextFormat { Tsv, Csv, Compact }

public static class GridTextTransfer
{
    public static string Export(SceneObject node, SceneObject grid, GridTextFormat format)
    {
        if (node.Kind != ObjectKind.GridText || grid.Kind != ObjectKind.Grid) throw new InvalidOperationException("グリッド文字を選択してください。");
        var glyphs = node.Glyphs.ToDictionary(g => (g.Column, g.Row), g => g.Text);
        var rows = new List<string>();
        for (int r = 0; r < grid.Rows; r++)
        {
            var cells = new List<string>();
            for (int c = 0; c < grid.Columns; c++)
            {
                var text = glyphs.GetValueOrDefault((c, r), "");
                ValidateCell(text, c, r);
                if (format == GridTextFormat.Compact)
                {
                    if (text.Contains('[') || text.Contains(']')) throw new FormatException($"{r + 1}行{c + 1}列に [ または ] があります。CSVまたはTSVを使用してください。");
                    cells.Add(StringInfo.ParseCombiningCharacters(text).Length == 1 ? text : "[" + text + "]");
                }
                else
                {
                    char delimiter = format == GridTextFormat.Csv ? ',' : '\t';
                    cells.Add(text.Contains(delimiter) || text.Contains('"') ? "\"" + text.Replace("\"", "\"\"") + "\"" : text);
                }
            }
            rows.Add(string.Join(format == GridTextFormat.Compact ? "" : format == GridTextFormat.Csv ? "," : "\t", cells));
        }
        // A row terminator preserves even the last empty row of a one-column board.
        return string.Join("\r\n", rows) + "\r\n";
    }

    public static string[][] Parse(string text, GridTextFormat format, int columns, int rows)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var result = format == GridTextFormat.Compact ? ParseCompact(text) : ParseDelimited(text, format == GridTextFormat.Csv ? ',' : '\t');
        if (result.Count != rows) throw new FormatException($"行数が一致しません。盤面は{rows}行、入力は{result.Count}行です。");
        for (int r = 0; r < result.Count; r++)
        {
            if (result[r].Length != columns) throw new FormatException($"{r + 1}行目の列数が一致しません。盤面は{columns}列、入力は{result[r].Length}列です。");
            for (int c = 0; c < columns; c++) ValidateCell(result[r][c], c, r);
        }
        return result.ToArray();
    }

    private static void ValidateCell(string text, int column, int row)
    {
        if (text.Any(char.IsControl)) throw new FormatException($"{row + 1}行{column + 1}列に改行・タブなどの制御文字があります。マス内の文字には使用できません。");
    }

    private static List<string[]> ParseCompact(string text)
    {
        if (text.EndsWith('\n')) text = text[..^1];
        var result = new List<string[]>();
        foreach (var line in text.Split('\n'))
        {
            var cells = new List<string>();
            for (int i = 0; i < line.Length;)
            {
                if (line[i] == '[')
                {
                    int end = line.IndexOf(']', i + 1);
                    if (end < 0) throw new FormatException($"{result.Count + 1}行目の [ が閉じられていません。");
                    string cell = line[(i + 1)..end];
                    if (cell.Contains('[')) throw new FormatException($"{result.Count + 1}行目で [ が入れ子になっています。[・] の文字にはCSVまたはTSVを使用してください。");
                    cells.Add(cell); i = end + 1;
                }
                else if (line[i] == ']') throw new FormatException($"{result.Count + 1}行目に対応する [ のない ] があります。CSVまたはTSVを使用してください。");
                else
                {
                    string cell = StringInfo.GetNextTextElement(line, i);
                    cells.Add(cell); i += cell.Length;
                }
            }
            result.Add(cells.ToArray());
        }
        return result;
    }

    private static List<string[]> ParseDelimited(string text, char delimiter)
    {
        var result = new List<string[]>(); var row = new List<string>(); var cell = new StringBuilder();
        bool quoted = false, closedQuote = false, start = true;
        void EndCell() { row.Add(cell.ToString()); cell.Clear(); closedQuote = false; start = true; }
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (quoted)
            {
                if (ch != '"') cell.Append(ch);
                else if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else { quoted = false; closedQuote = true; }
                continue;
            }
            if (ch == delimiter) EndCell();
            else if (ch == '\n') { EndCell(); result.Add(row.ToArray()); row.Clear(); }
            else if (ch == '"' && start) { quoted = true; start = false; }
            else
            {
                if (closedQuote || ch == '"') throw new FormatException($"{result.Count + 1}行{row.Count + 1}列の引用符が不正です。文字中の引用符は二重にし、マス全体を引用符で囲んでください。");
                cell.Append(ch); start = false;
            }
        }
        if (quoted) throw new FormatException("閉じられていない引用符があります。");
        if (text.Length == 0 || !text.EndsWith('\n')) { EndCell(); result.Add(row.ToArray()); }
        return result;
    }
}

public sealed partial class SceneDocument
{
    public void ImportGridText(SceneObject node, string text, GridTextFormat format)
    {
        if (Find(node.Id) != node || node.Kind != ObjectKind.GridText || GridFor(node) is not { } grid) throw new InvalidOperationException("対象のグリッド文字がありません。");
        if (!Editable(node) || !Shown(node)) throw new InvalidOperationException("非表示またはロック中のグリッド文字には反映できません。");
        var cells = GridTextTransfer.Parse(text, format, grid.Columns, grid.Rows);
        var old = node.Glyphs.ToDictionary(g => (g.Column, g.Row));
        var glyphs = new List<Glyph>();
        for (int r = 0; r < grid.Rows; r++)
            for (int c = 0; c < grid.Columns; c++)
            {
                var value = cells[r][c]; if (value.Length == 0) continue;
                var previous = old.GetValueOrDefault((c, r));
                glyphs.Add(new() { Column = c, Row = r, Text = value, Font = previous?.Font, Size = previous?.Size,
                    InCellText = previous?.InCellText == true || StringInfo.ParseCombiningCharacters(value).Length > 1 });
            }
        node.Glyphs = glyphs;
    }
}
