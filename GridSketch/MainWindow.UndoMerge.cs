namespace GridSketch;

public partial class MainWindow
{
    private string? undoMergeKey, undoMergeAfter;
    private SceneDocument? undoMergeDocument;

    private void BreakUndoMerge()
    {
        undoMergeKey = undoMergeAfter = null; undoMergeDocument = null;
    }

    private void RecordUndo(string before, string? key)
    {
        bool merge = key is not null && key == undoMergeKey && ReferenceEquals(document, undoMergeDocument)
            && before == undoMergeAfter && undo.Count > 0 && redo.Count == 0;
        if (!merge)
        {
            undo.Push(before);
            if (undo.Count > 30)
            {
                var keep = undo.Take(30).Reverse().ToList(); undo.Clear();
                foreach (var state in keep) undo.Push(state);
            }
        }
        redo.Clear();
        undoMergeKey = key; undoMergeDocument = document;
        undoMergeAfter = key is null ? null : document.Snapshot();
    }
}
