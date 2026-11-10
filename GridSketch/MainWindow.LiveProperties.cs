using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace GridSketch;

public partial class MainWindow
{
    private string? propertyBefore;
    private FrameworkElement? propertyEditor;
    private Action? resetPropertyEditor;
    private bool updatingProperty;
    private bool propertyComposing;
    private bool propertyDragging;
    private string? propertyMergeKey;

    private void CommitPropertyEdit()
    {
        if (updatingProperty) return;
        var before = propertyBefore;
        var mergeKey = propertyMergeKey; propertyMergeKey = null;
        propertyBefore = null; propertyEditor = null;
        var reset = resetPropertyEditor; resetPropertyEditor = null;
        updatingProperty = true;
        try { reset?.Invoke(); } finally { updatingProperty = false; }
        if (before is not null && before != document.Snapshot()) RecordUndo(before, mergeKey);
    }

    private void RestorePropertySnapshot(string snapshot)
    {
        // Keep editor closures pointing at the same nodes and styles after validation fails.
        var restored = SceneDocument.Restore(snapshot);
        foreach (var node in document.All().ToList())
        {
            if (restored.Find(node.Id) is not { } source) continue;
            foreach (var p in typeof(SceneObject).GetProperties().Where(p => p.CanWrite && p.Name is not ("Children" or "Style")))
                p.SetValue(node, p.GetValue(source));
            foreach (var p in typeof(LayerStyle).GetProperties().Where(p => p.CanWrite)) p.SetValue(node.Style, p.GetValue(source.Style));
        }
    }

    private void PreviewProperty(FrameworkElement editor, Action setter, Action? reset = null)
    {
        if (updatingProperty || propertyComposing) return;
        if (propertyEditor != editor)
        {
            CommitPropertyEdit(); propertyEditor = editor; propertyBefore = document.Snapshot();
            var name = System.Windows.Automation.AutomationProperties.GetName(editor);
            propertyMergeKey = "property:" + Selected.Id + ":" + (string.IsNullOrEmpty(name)
                ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(editor).ToString() : name);
        }
        resetPropertyEditor = reset;
        var before = document.Snapshot(); var node = Selected;
        updatingProperty = true;
        try
        {
            RequireEditable(node);
            var old = (node.Columns, node.Rows, node.CellSize, node.SeparateCellHeight, node.Margin, node.MarginY);
            setter();
            if (node.Kind == ObjectKind.Grid)
            {
                var next = (node.Columns, node.Rows, node.CellSize, node.SeparateCellHeight, node.Margin, node.MarginY);
                (node.Columns, node.Rows, node.CellSize, node.SeparateCellHeight, node.Margin, node.MarginY) = old;
                document.ResizeGrid(node, next.Columns, next.Rows, next.CellSize, next.Margin, next.MarginY, next.SeparateCellHeight);
            }
            document.PrepareGroupLayouts(); document.ClearMissingStateObjects(); document.Validate();
            RefreshSurface(); editor.ClearValue(Control.BorderBrushProperty); editor.ToolTip = null;
            foreach (var item in LayerItems(Layers)) if (item.Tag is string id && document.Find(id) is { } n) item.Header = n.Label;
            UpdateTabHeaders(); Title = $"{(Dirty ? "* " : "")}{(path is null ? "無題" : System.IO.Path.GetFileName(path))} — GridSketch";
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException or InvalidOperationException or NotSupportedException or System.IO.InvalidDataException)
        {
            RestorePropertySnapshot(before); editor.SetValue(Control.BorderBrushProperty, Brushes.IndianRed); editor.ToolTip = ex.Message;
        }
        finally { updatingProperty = false; }
    }
    private void LiveText(TextBox box, Action setter)
    {
        var owner = Selected; var ownerDocument = document;
        string valid = box.Text;
        void Reset() { box.Text = valid; box.ClearValue(Control.BorderBrushProperty); box.ToolTip = null; }
        void Preview()
        {
            if (document != ownerDocument || Selected != owner) return;
            PreviewProperty(box, setter, Reset);
            if (box.ToolTip is null) valid = box.Text;
        }
        box.TextChanged += (_, _) => { if (updatingProperty) { valid = box.Text; return; } if (!propertyComposing) Preview(); };
        TextCompositionManager.AddPreviewTextInputStartHandler(box, (_, _) => propertyComposing = true);
        box.AddHandler(TextCompositionManager.TextInputEvent, new TextCompositionEventHandler((_, _) =>
        { propertyComposing = false; box.Dispatcher.BeginInvoke(new Action(() => { if (box.IsKeyboardFocusWithin) Preview(); })); }), true);
        box.LostKeyboardFocus += (_, _) =>
        {
            propertyComposing = false;
            if (!updatingProperty && !propertyDragging && document == ownerDocument && Selected == owner)
            { Preview(); CommitPropertyEdit(); }
        };
        box.PreviewKeyDown += (_, e) =>
        {
            if (propertyComposing || e.Key == Key.ImeProcessed) return;
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.Z or Key.Y)
            {
                if (e.Key == Key.Z) Undo(); else Redo(); e.Handled = true; return;
            }
            if (e.Key == Key.Escape && propertyBefore is { } before)
            {
                propertyBefore = null; propertyEditor = null; resetPropertyEditor = null;
                document = SceneDocument.Restore(before); RefreshAll(); e.Handled = true;
            }
            else if (e.Key == Key.Enter && !box.AcceptsReturn) { CommitPropertyEdit(); e.Handled = true; }
        };
        // Numeric sliders edit this text box. Finish once the mouse gesture ends.
        if (box.Parent is Grid row)
            foreach (var slider in row.Children.OfType<Slider>())
            {
                slider.PreviewMouseLeftButtonDown += (_, _) => { CommitPropertyEdit(); propertyDragging = true; };
                slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => { propertyDragging = false; CommitPropertyEdit(); }), true);
                slider.PreviewMouseLeftButtonUp += (_, _) => { propertyDragging = false; CommitPropertyEdit(); };
                slider.LostMouseCapture += (_, _) => { if (!slider.IsMouseCaptureWithin) { propertyDragging = false; CommitPropertyEdit(); } };
            }
    }
    private void LiveChoice(ComboBox combo, Action setter)
    {
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is null || updatingProperty) return;
            PreviewProperty(combo, setter); CommitPropertyEdit();
        };
        if (combo.IsEditable)
        {
            combo.Loaded += (_, _) =>
            {
                if (combo.Template.FindName("PART_EditableTextBox", combo) is TextBox input) LiveText(input, setter);
            };
        }
    }
    private void ImmediateProperty(FrameworkElement editor, Action setter)
    { PreviewProperty(editor, setter); CommitPropertyEdit(); }
}
