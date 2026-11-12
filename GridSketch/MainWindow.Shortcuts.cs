using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace GridSketch;

public partial class MainWindow
{
    private void EditorShortcutKey(object sender, KeyEventArgs e)
    {
        if (e.Handled || e.IsRepeat || gridComposing || e.Key == Key.ImeProcessed) return;
        bool typing = Keyboard.FocusedElement is TextBoxBase or PasswordBox;
        if (HandleEditorShortcut(e.Key, Keyboard.Modifiers, typing)) e.Handled = true;
    }

    private bool HandleEditorShortcut(Key key, ModifierKeys modifiers, bool typing)
    {
        if (activeTab is null) return false;
        if (modifiers == ModifierKeys.Control && key is Key.W or Key.F4)
        { CloseTabClick(this, new RoutedEventArgs()); return true; }
        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.S)
        { Try(SaveAllTabs); return true; }
        if (key == Key.Tab && modifiers is ModifierKeys.Control or (ModifierKeys.Control | ModifierKeys.Shift))
        {
            int step = modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1;
            Try(() => SwitchTab(editorTabs[(editorTabs.IndexOf(activeTab) + step + editorTabs.Count) % editorTabs.Count]));
            return true;
        }
        if (typing) return false;
        if (modifiers == ModifierKeys.Control && key == Key.D)
        {
            Try(() =>
            {
                var nodes = SelectedLayers();
                if (document.DuplicateSelectionError(nodes) is { } error) { Status.Text = error; return; }
                if (nodes.Count == 1) { DuplicateLayer(); return; }
                Change(() =>
                {
                    var copies = document.DuplicateInto(nodes, document.Parent(nodes[0])!);
                    selectedId = copies[0].Id; layerSelection.Clear();
                    foreach (var copy in copies) layerSelection.Add(copy.Id);
                });
            });
            return true;
        }
        if (modifiers == ModifierKeys.Control && key is Key.D0 or Key.NumPad0)
        { Zoom.Value = 1; return true; }
        if (modifiers != ModifierKeys.None) return false;
        if (key is Key.V or Key.E)
        {
            ModeChanged(key == Key.V ? MoveModeButton : CellModeButton, new RoutedEventArgs());
            Viewport.Focus(); return true;
        }
        if (key == Key.F2 && Layers.IsKeyboardFocusWithin)
        {
            var item = LayerItems(Layers).FirstOrDefault(i => Equals(i.Tag, selectedId));
            if (item is not null) BeginLayerNameEdit(item);
            return true;
        }
        if (key == Key.Home)
        {
            var bounds = Surface.Renderer!.OutputBounds;
            Zoom.Value = Math.Clamp(Math.Min(Math.Max(1, Viewport.ViewportWidth - 80) / bounds.Width,
                Math.Max(1, Viewport.ViewportHeight - 80) / bounds.Height), Zoom.Minimum, Zoom.Maximum);
            CenterClick(this, new RoutedEventArgs()); return true;
        }
        if (key == Key.Escape)
        {
            if (dragSnapshot is { } snapshot)
            {
                dragStart = null; paintStroke = null; paintGrid = null; dragSnapshot = null; dragContentOrigin = null; lineHandleDrag = null;
                document = SceneDocument.Restore(snapshot); Surface.ReleaseMouseCapture(); Surface.Cursor = null; RefreshAll();
            }
            GridInput.Visibility = Visibility.Collapsed; Viewport.Focus(); return true;
        }
        return false;
    }

    private void SaveAllTabs()
    {
        CommitPropertyEdit(); FinishDrag(); CaptureTab();
        var original = activeTab;
        int count = 0;
        try
        {
            // Apply part edits before saving the projects containing their libraries.
            foreach (var tab in editorTabs.OrderByDescending(t => t.Owner is not null).ToList())
            {
                SwitchTab(tab);
                if (!Dirty && (tab.Path is not null || tab.Owner is not null)) continue;
                if (!Save(false)) { Status.Text = $"すべて保存を中断しました（{count}件保存済み）。"; return; }
                count++;
            }
            Status.Text = $"すべて保存しました（{count}件）。";
        }
        finally { if (original is not null && editorTabs.Contains(original)) SwitchTab(original); }
    }
    private void SaveAllClick(object sender, RoutedEventArgs e) => Try(SaveAllTabs);
}
