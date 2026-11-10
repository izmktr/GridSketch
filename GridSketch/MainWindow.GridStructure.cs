using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GridSketch;

public partial class MainWindow
{
    private void GridContextMenu(object sender, MouseButtonEventArgs e)
    {
        if (!cellEditMode || !document.Shown(Selected)) return;
        var grid = document.GridFor(Selected);
        if (grid is null || !document.Editable(grid)) return;
        var point = Surface.ToLocal(grid, e.GetPosition(Surface));
        int column = (int)Math.Floor(point.X / grid.CellSize);
        int row = (int)Math.Floor(point.Y / grid.CellHeight);
        if (column < 0 || row < 0 || column >= grid.Columns || row >= grid.Rows) return;
        CommitPropertyEdit();
        var first = Surface.ActiveCell ?? new CellPosition(column, row);
        var last = Surface.RangeEnd ?? first;
        int left = Math.Min(first.Column, last.Column), right = Math.Max(first.Column, last.Column);
        int top = Math.Min(first.Row, last.Row), bottom = Math.Max(first.Row, last.Row);
        if (column < left || column > right || row < top || row > bottom)
        {
            left = right = column; top = bottom = row;
        }
        Surface.ActiveCell = new(left, top); Surface.RangeEnd = new(right, bottom);
        Surface.InvalidateVisual();
        var menu = new ContextMenu { PlacementTarget = Surface };
        bool editable = !SceneDocument.GridMembers(grid).Any(n => !document.Editable(n));
        void Item(string title, bool rows, int start, int count, bool delete)
        {
            int size = rows ? grid.Rows : grid.Columns;
            var item = new MenuItem { Header = title, IsEnabled = editable && (delete ? size > count : size + count <= 200) };
            if (!editable) item.ToolTip = "グリッド内のロックを解除してください。";
            item.MouseEnter += (_, _) =>
            {
                Surface.GridStructurePreview = delete
                    ? (grid, rows ? new Rect(0, top * grid.CellHeight, grid.Columns * grid.CellSize, count * grid.CellHeight)
                        : new Rect(left * grid.CellSize, 0, count * grid.CellSize, grid.Rows * grid.CellHeight))
                    : null;
                Surface.InvalidateVisual();
            };
            item.Click += (_, _) => Change(() =>
            {
                document.EditGridStructure(grid, rows, start, count, delete);
                Surface.ActiveCell = null; Surface.RangeEnd = null;
                if (document.Find(selectedId) is null) selectedId = grid.Id;
            });
            menu.Items.Add(item);
        }
        int rowCount = bottom - top + 1, columnCount = right - left + 1;
        Item(rowCount == 1 ? "上に行を追加" : $"上に{rowCount}行を追加", true, top, rowCount, false);
        Item(rowCount == 1 ? "下に行を追加" : $"下に{rowCount}行を追加", true, bottom + 1, rowCount, false);
        Item(columnCount == 1 ? "左に列を追加" : $"左に{columnCount}列を追加", false, left, columnCount, false);
        Item(columnCount == 1 ? "右に列を追加" : $"右に{columnCount}列を追加", false, right + 1, columnCount, false);
        menu.Items.Add(new Separator());
        Item(rowCount == 1 ? "この行を削除" : $"選択した{rowCount}行を削除", true, top, rowCount, true);
        Item(columnCount == 1 ? "この列を削除" : $"選択した{columnCount}列を削除", false, left, columnCount, true);
        menu.Closed += (_, _) => { Surface.GridStructurePreview = null; Surface.InvalidateVisual(); };
        menu.IsOpen = true;
        e.Handled = true;
    }
}
