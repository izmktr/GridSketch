using System.Windows;
using System.Windows.Input;

namespace GridSketch;

public partial class MainWindow
{
    private bool DeleteSelectedGridContents()
    {
        var node = Selected;
        if (!cellEditMode || Surface.CleanPreview || gridComposing || Surface.ActiveCell is not { } start
            || !document.Editable(node) || !document.Shown(node) || document.GridFor(node) is not { } grid
            || node.Kind is not (ObjectKind.GridText or ObjectKind.Border)) return false;
        if (node.Kind == ObjectKind.Border && Surface.RangeEnd is null) return false;
        var end = Surface.RangeEnd ?? start;
        int left = Math.Min(start.Column, end.Column), right = Math.Max(start.Column, end.Column);
        int top = Math.Min(start.Row, end.Row), bottom = Math.Max(start.Row, end.Row);
        ChangeGridContent(() =>
        {
            if (node.Kind == ObjectKind.GridText)
                node.Glyphs.RemoveAll(g => g.Column >= left && g.Column <= right && g.Row >= top && g.Row <= bottom);
            else
            {
                node.Edges = SceneDocument.BorderEdges(node, grid).ToHashSet(); node.Pattern = "個別";
                int extra = node.ConnectCellCenters ? 0 : 1;
                bool Inside(Point p) => p.X >= left && p.X <= right + extra && p.Y >= top && p.Y <= bottom + extra;
                node.Edges.RemoveWhere(edge => Inside(edge.Start) && Inside(edge.End));
            }
        });
        Status.Text = "選択範囲の内容を削除しました。Ctrl+Zで元に戻せます。";
        return true;
    }

    private void GridRangeDeleteKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || Keyboard.Modifiers != ModifierKeys.None || e.IsRepeat
            || !(Viewport.IsKeyboardFocusWithin || GridInput.IsKeyboardFocusWithin)
            || GridInput.Text.Length > 0) return;
        if (DeleteSelectedGridContents()) e.Handled = true;
    }
}
