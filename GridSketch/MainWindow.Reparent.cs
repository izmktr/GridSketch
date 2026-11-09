using System.Windows;

namespace GridSketch;

public partial class MainWindow
{
    private void ChooseParent(SceneObject node)
        => ChooseParents([node]);

    private void ChooseSelectedParents() => ChooseParents(SelectedLayers());

    private void ChooseDuplicateParent()
    {
        CommitPropertyEdit(); var nodes = SelectedLayers();
        if (document.ReparentSelectionError(nodes) is { } error) throw new InvalidOperationException(error);
        var dialog = new ReparentDialog(document, nodes, duplicate: true) { Owner = this };
        bool? accepted;
        // Restoring keyboard focus after the modal must not collapse the multi-selection.
        bool wasSelecting = selectingLayers; selectingLayers = true;
        try { accepted = dialog.ShowDialog(); }
        finally { selectingLayers = wasSelecting; }
        if (accepted != true || dialog.SelectedParentId is not { } id) return;
        Change(() =>
        {
            var target = document.Find(id) ?? throw new InvalidOperationException("複製先が見つかりません。");
            var copies = document.DuplicateInto(nodes, target);
            selectedId = copies[0].Id; layerAnchor = selectedId;
            layerSelection.Clear(); foreach (var copy in copies) layerSelection.Add(copy.Id);
        });
    }

    private void ChooseParents(IReadOnlyList<SceneObject> nodes)
    {
        CommitPropertyEdit();
        if (document.ReparentSelectionError(nodes) is { } error) throw new InvalidOperationException(error);
        var dialog = new ReparentDialog(document, nodes) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedParentId is not { } id) return;
        Change(() =>
        {
            var target = document.Find(id) ?? throw new InvalidOperationException("移動先が見つかりません。");
            document.ReparentMany(nodes, target);
            layerSelection.Clear(); foreach (var node in nodes) layerSelection.Add(node.Id);
        });
    }
}
