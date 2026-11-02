using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GridSketch;

public partial class MainWindow
{
    private sealed class EditorTab
    {
        public SceneDocument Document = new();
        public string? Path, SelectedId;
        public string Saved = "";
        public List<string> Undo = [], Redo = [], Selection = [];
        public double Zoom = 1;
        public Point Center;
        public bool HasView, CellMode = true, TextVertical, TextInCell, InputStopped;
        public string? Anchor;
        public CellPosition? Cell, Range;
        public EditorTab? Owner;
        public string? PartId, PartVersion;
        public string Name = "無題";
    }
    private readonly List<EditorTab> editorTabs = [];
    private EditorTab? activeTab;
    private bool updatingTabs;

    private void CaptureTab()
    {
        if (activeTab is null) return;
        activeTab.Document = document; activeTab.Path = path; activeTab.SelectedId = selectedId; activeTab.Saved = savedState;
        activeTab.Undo = undo.Reverse().ToList(); activeTab.Redo = redo.Reverse().ToList();
        activeTab.Selection = layerSelection.ToList(); activeTab.CellMode = cellEditMode;
        activeTab.Anchor = layerAnchor; activeTab.TextVertical = Surface.TextVertical; activeTab.TextInCell = Surface.TextInCell; activeTab.InputStopped = gridInputStopped;
        activeTab.Cell = Surface.ActiveCell; activeTab.Range = Surface.RangeEnd; activeTab.Zoom = Zoom.Value;
        activeTab.Center = new Point((Viewport.HorizontalOffset + Viewport.ViewportWidth / 2) / Zoom.Value - Surface.OriginX,
            (Viewport.VerticalOffset + Viewport.ViewportHeight / 2) / Zoom.Value - Surface.OriginY);
        activeTab.HasView = true;
    }
    private void AddProjectTab(SceneDocument scene, string? file = null)
    {
        if (activeTab is null && editorTabs.Count == 0)
        {
            activeTab = new EditorTab(); editorTabs.Add(activeTab); CaptureTab();
        }
        var tab = new EditorTab { Document = scene, Path = file, SelectedId = scene.Canvas.Id, Saved = scene.Snapshot() };
        editorTabs.Add(tab); SwitchTab(tab);
    }
    private void SwitchTab(EditorTab tab)
    {
        CommitPropertyEdit();
        if (tab == activeTab) { UpdateTabHeaders(); return; }
        FinishDrag(); Surface.ReleaseMouseCapture(); EndPan();
        if (!gridComposing && GridInput.Text.Length > 0 && CanTypeInGrid) CommitGridInput(GridInput.Text);
        clearingGridInput = true; GridInput.Clear(); clearingGridInput = false;
        Keyboard.ClearFocus(); GridInput.Visibility = Visibility.Collapsed;
        CaptureTab(); activeTab = tab;
        document = tab.Document; path = tab.Path; savedState = tab.Saved; selectedId = tab.SelectedId;
        undo.Clear(); foreach (var state in tab.Undo) undo.Push(state);
        redo.Clear(); foreach (var state in tab.Redo) redo.Push(state);
        layerSelection.Clear(); foreach (var id in tab.Selection) layerSelection.Add(id); layerAnchor = tab.Anchor;
        Surface.TextVertical = tab.TextVertical; Surface.TextInCell = tab.TextInCell; gridInputStopped = tab.InputStopped;
        cellEditMode = tab.CellMode; CellModeButton.IsChecked = cellEditMode; MoveModeButton.IsChecked = !cellEditMode;
        Surface.Selected = null; Surface.ActiveCell = null; Surface.RangeEnd = null; Surface.HoveredEdge = null;
        RefreshAll(); Surface.ActiveCell = tab.Cell; Surface.RangeEnd = tab.Range;
        Zoom.Value = tab.Zoom; Surface.LayoutTransform = new ScaleTransform(tab.Zoom, tab.Zoom);
        if (tab.HasView)
        {
            Surface.EnsureZoomSpace(tab.Center, new Size(Viewport.ViewportWidth / tab.Zoom, Viewport.ViewportHeight / tab.Zoom));
            Viewport.UpdateLayout();
            Viewport.ScrollToHorizontalOffset((tab.Center.X + Surface.OriginX) * tab.Zoom - Viewport.ViewportWidth / 2);
            Viewport.ScrollToVerticalOffset((tab.Center.Y + Surface.OriginY) * tab.Zoom - Viewport.ViewportHeight / 2);
        }
        else CenterClick(this, new RoutedEventArgs());
        Viewport.UpdateLayout(); Surface.InvalidateVisual(); UpdateTabHeaders();
    }
    private void UpdateTabHeaders()
    {
        if (DocumentTabs is null || updatingTabs) return;
        updatingTabs = true;
        try
        {
            DocumentTabs.Items.Clear();
            foreach (var tab in editorTabs)
            {
                var scene = tab == activeTab ? document : tab.Document;
                bool dirty = scene.Snapshot() != (tab == activeTab ? savedState : tab.Saved);
                string? file = tab == activeTab ? path : tab.Path;
                string name = tab.Owner is not null ? "部品：" + tab.Name : file is null ? "無題" : System.IO.Path.GetFileName(file);
                var header = new StackPanel { Orientation = Orientation.Horizontal };
                header.Children.Add(new TextBlock { Text = (dirty ? "* " : "") + name, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 8, 0) });
                var close = new Button { Content = "×", Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(0), ToolTip = "タブを閉じる" };
                close.Click += (_, e) => { e.Handled = true; Try(() => CloseEditorTab(tab)); };
                header.Children.Add(close);
                var item = new TabItem { Header = header, Tag = tab, ToolTip = file ?? name };
                DocumentTabs.Items.Add(item); if (tab == activeTab) DocumentTabs.SelectedItem = item;
            }
            ApplyPartButton.Visibility = activeTab?.Owner is null ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { updatingTabs = false; }
    }
    private void DocumentTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!updatingTabs && ReferenceEquals(e.Source, DocumentTabs) && DocumentTabs.SelectedItem is TabItem { Tag: EditorTab tab }) Try(() => SwitchTab(tab));
    }
    private bool CloseEditorTab(EditorTab tab)
    {
        foreach (var child in editorTabs.Where(t => t.Owner == tab).ToList()) if (!CloseEditorTab(child)) return false;
        SwitchTab(tab); if (!CanDiscard()) return false;
        int index = editorTabs.IndexOf(tab); editorTabs.Remove(tab); activeTab = null;
        if (editorTabs.Count == 0)
        {
            var scene = new SceneDocument(); scene.Canvas.Children.Add(SceneDocument.CreateGrid("基本の方眼紙"));
            var fresh = new EditorTab { Document = scene, Saved = scene.Snapshot(), SelectedId = scene.Canvas.Id };
            editorTabs.Add(fresh);
        }
        SwitchTab(editorTabs[Math.Min(index, editorTabs.Count - 1)]); return true;
    }
    private bool CanCloseAllTabs()
    {
        if (editorTabs.Count == 0) return CanDiscard();
        var original = activeTab;
        foreach (var tab in editorTabs.OrderByDescending(t => t.Owner is not null).ToList())
        {
            SwitchTab(tab); if (!CanDiscard()) return false;
        }
        if (original is not null) SwitchTab(original); return true;
    }
    private void CloseTabClick(object sender, RoutedEventArgs e) { if (activeTab is not null) Try(() => CloseEditorTab(activeTab)); }

    private static string PartVersion(LibraryPart part) => System.Text.Json.JsonSerializer.Serialize(part, SceneDocument.JsonOptions);
    private void EditPartTab(LibraryPart part)
    {
        if (activeTab?.Owner is not null) throw new InvalidOperationException("部品編集は元のプロジェクトのライブラリから開いてください。");
        CaptureTab(); var owner = activeTab!;
        var existing = editorTabs.FirstOrDefault(t => t.Owner == owner && t.PartId == part.Id);
        if (existing is not null) { SwitchTab(existing); return; }
        var scene = new SceneDocument(); var root = part.CreateCopy(); scene.Canvas.Children.Add(root);
        var tab = new EditorTab { Document = scene, SelectedId = root.Id, Saved = scene.Snapshot(), Owner = owner,
            PartId = part.Id, PartVersion = PartVersion(part), Name = part.Name };
        editorTabs.Add(tab); SwitchTab(tab);
    }
    private bool ApplyPartEdit()
    {
        bool applied = false;
        Try(() =>
        {
            FinishDrag(); var tab = activeTab!; var owner = tab.Owner ?? throw new InvalidOperationException("部品編集タブではありません。");
            if (document.Canvas.Children.Count != 1 || document.Canvas.Children[0].Kind != ObjectKind.Group)
                throw new InvalidOperationException("部品はキャンバス直下の1つのグループにまとめてください。");
            var part = owner.Document.Parts.FirstOrDefault(p => p.Id == tab.PartId) ?? throw new InvalidOperationException("元のライブラリから部品が削除されています。");
            if (PartVersion(part) != tab.PartVersion) throw new InvalidOperationException("元の部品が別の操作で更新されています。編集内容をプロジェクトとして保存し、部品タブを開き直してください。");
            var replacement = LibraryPart.FromGroup(document.Canvas.Children[0], part.Name);
            owner.Undo.Add(owner.Document.Snapshot()); owner.Redo.Clear(); part.Template = replacement.Template;
            if (owner.Undo.Count > 30) owner.Undo.RemoveAt(0);
            tab.PartVersion = PartVersion(part); savedState = document.Snapshot(); tab.Saved = savedState;
            applied = true; UpdateTabHeaders(); Status.Text = "ライブラリへ反映しました。元のプロジェクトを保存すると部品も保存されます。配置済みのコピーは変更しません。";
        });
        return applied;
    }
    private void ApplyPartClick(object sender, RoutedEventArgs e) => ApplyPartEdit();
}
