using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace GridSketch;

public partial class MainWindow
{
    private bool gridComposing;
    private bool gridInputStopped;
    private bool gridInputQueued;
    private bool clearingGridInput;

    private bool CanTypeInGrid => cellEditMode && Selected.Kind == ObjectKind.GridText
        && Surface.ActiveCell is { } cell && (Surface.RangeEnd is null || Surface.RangeEnd == cell)
        && document.Editable(Selected);

    private void SelectTextCell(CellPosition cell)
    {
        gridInputStopped = false;
        bool same = Surface.ActiveCell == cell && Surface.RangeEnd is null;
        bool wasVertical = Surface.TextVertical;
        Surface.TextVertical = same && !Surface.TextVertical && !Surface.TextInCell;
        Surface.TextInCell = same && wasVertical;
        Surface.ActiveCell = cell; Surface.RangeEnd = null; Surface.Preview = [];
        Surface.InvalidateVisual();
        Status.Text = $"{cell.Column + 1}列・{cell.Row + 1}行：{(Surface.TextInCell ? "□ 同じマスへ追記" : Surface.TextVertical ? "下へ入力" : "右へ入力")}。同じマスをクリックすると右 → 下 → □に切り替えます。";
        FocusGridInput();
    }

    private void FocusGridInput()
    {
        if (!CanTypeInGrid) return;
        var grid = document.GridFor(Selected)!; var cell = Surface.ActiveCell!.Value;
        var world = document.World(Selected).Transform(new Point(cell.Column * grid.CellSize, cell.Row * grid.CellHeight));
        var anchor = Surface.TranslatePoint(new Point(world.X + Surface.OriginX, world.Y + Surface.OriginY), InputOverlay);
        Canvas.SetLeft(GridInput, anchor.X); Canvas.SetTop(GridInput, anchor.Y);
        GridInput.FontSize = Math.Max(8, Math.Min(Selected.FontSize, Math.Min(grid.CellSize, grid.CellHeight) * 0.7) * Zoom.Value);
        GridInput.Height = Math.Max(24, grid.CellHeight * Zoom.Value);
        GridInput.Visibility = Visibility.Visible;
        GridInput.Focus();
    }

    private bool OpenTextCell(SceneObject grid, CellPosition cell) => cell.Column >= 0 && cell.Row >= 0
        && cell.Column < grid.Columns && cell.Row < grid.Rows;

    // Validate the whole IME commit before making a single undoable change.
    private void CommitGridInput(string input)
    {
        if (!CanTypeInGrid || string.IsNullOrEmpty(input)) return;
        if (gridInputStopped) throw new InvalidOperationException("グリッドの端で停止しています。次の入力先を選択してください。");
        var node = Selected; var cell = Surface.ActiveCell!.Value;
        if (Surface.TextInCell)
        {
            if (input.Any(char.IsControl)) throw new InvalidOperationException("改行や制御文字は入力できません。");
            if (!OpenTextCell(document.GridFor(node)!, cell)) throw new InvalidOperationException("このマスには入力できません。");
            ChangeGridContent(() =>
            {
                RequireEditable(node);
                var glyph = node.Glyphs.SingleOrDefault(g => g.Column == cell.Column && g.Row == cell.Row);
                if (glyph is null) { glyph = new Glyph { Column = cell.Column, Row = cell.Row }; node.Glyphs.Add(glyph); }
                glyph.Text += input; glyph.InCellText = true;
            });
            return;
        }
        var plan = document.PlanText(node, cell.Column, cell.Row, input, Surface.TextVertical);
        ChangeGridContent(() =>
        {
            RequireEditable(node);
            foreach (var glyph in plan)
            {
                node.Glyphs.RemoveAll(g => g.Column == glyph.Column && g.Row == glyph.Row);
                node.Glyphs.Add(glyph);
            }
        });
        var last = plan[^1]; var grid = document.GridFor(Selected)!;
        var next = new CellPosition(last.Column + (Surface.TextVertical ? 0 : 1), last.Row + (Surface.TextVertical ? 1 : 0));
        bool canAdvance = OpenTextCell(grid, next);
        gridInputStopped = !canAdvance;
        Surface.ActiveCell = canAdvance ? next : new CellPosition(last.Column, last.Row);
        Surface.InvalidateVisual();
        if (!canAdvance) Status.Text = "グリッドの端で停止しました。次の入力先を選択してください。";
    }

    private void GridInputText(object sender, TextCompositionEventArgs e)
    {
        // Let TextBox/TSF finish its edit first. Do not handle or clear a composition event.
        gridComposing = false;
        QueueGridInput();
    }

    private void GridInputChanged(object sender, TextChangedEventArgs e)
    {
        if (!clearingGridInput && !gridComposing) QueueGridInput();
    }

    private void QueueGridInput()
    {
        if (gridInputQueued) return;
        gridInputQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            gridInputQueued = false;
            if (gridComposing) return;
            var input = GridInput.Text;
            if (input.Length == 0) return;
            try { CommitGridInput(input); }
            catch (InvalidOperationException ex) { Status.Text = ex.Message; }
            finally
            {
                clearingGridInput = true;
                GridInput.Clear();
                clearingGridInput = false;
            }
            if (GridInput.IsKeyboardFocused) FocusGridInput();
        }));
    }

    private void ChangeGridContent(Action action)
    {
        CommitPropertyEdit();
        var before = document.Snapshot();
        try { action(); document.Validate(); }
        catch { document = SceneDocument.Restore(before); Surface.Selected = Selected; RefreshSurface(); throw; }
        if (before != document.Snapshot()) RememberUndo(before);
        Surface.Preview = []; RefreshSurface();
        UpdateTabHeaders();
        Title = $"{(Dirty ? "* " : "")}{(path is null ? "無題" : System.IO.Path.GetFileName(path))} — GridSketch";
        CommandManager.InvalidateRequerySuggested();
    }

    private void EditGridKey(Key key)
    {
        if (!CanTypeInGrid) return;
        var node = Selected; var grid = document.GridFor(node)!; var cell = Surface.ActiveCell!.Value;
        if (Surface.TextInCell && key == Key.Back)
        {
            ChangeGridContent(() =>
            {
                RequireEditable(node);
                var glyph = node.Glyphs.SingleOrDefault(g => g.Column == cell.Column && g.Row == cell.Row);
                if (glyph is null) return;
                var starts = System.Globalization.StringInfo.ParseCombiningCharacters(glyph.Text);
                if (starts.Length <= 1) node.Glyphs.Remove(glyph);
                else glyph.Text = glyph.Text[..starts[^1]];
            });
            gridInputStopped = false; return;
        }
        var next = key switch
        {
            Key.Left => new CellPosition(cell.Column - 1, cell.Row),
            Key.Right => new CellPosition(cell.Column + 1, cell.Row),
            Key.Up => new CellPosition(cell.Column, cell.Row - 1),
            Key.Down => new CellPosition(cell.Column, cell.Row + 1),
            Key.Back => new CellPosition(cell.Column - (Surface.TextVertical ? 0 : 1), cell.Row - (Surface.TextVertical ? 1 : 0)),
            _ => cell
        };
        if (key is Key.Back && !OpenTextCell(grid, next)) return;
        if (next.Column < 0 || next.Row < 0 || next.Column >= grid.Columns || next.Row >= grid.Rows) return;
        if (key is Key.Back or Key.Delete)
            ChangeGridContent(() => { RequireEditable(node); node.Glyphs.RemoveAll(g => g.Column == next.Column && g.Row == next.Row); });
        Surface.ActiveCell = next; Surface.RangeEnd = null; Surface.InvalidateVisual();
        gridInputStopped = false;
    }

    private void GridInputKey(object sender, KeyEventArgs e)
    {
        // Leave candidate navigation, conversion and confirmation entirely to the IME.
        if (gridComposing || e.Key == Key.ImeProcessed || !CanTypeInGrid) return;
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.Z or Key.Y)
        {
            if (e.Key == Key.Z) Undo(); else Redo();
            gridInputStopped = false;
            FocusGridInput(); e.Handled = true; return;
        }
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Back or Key.Delete)
        {
            EditGridKey(e.Key); FocusGridInput(); e.Handled = true;
        }
        else if (e.Key == Key.Escape) { GridInput.Visibility = Visibility.Collapsed; e.Handled = true; }
    }

    private void GridInputLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        gridComposing = false; GridInput.Clear(); GridInput.Visibility = Visibility.Collapsed;
    }
}
