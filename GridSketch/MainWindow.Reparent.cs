using System.Windows;

namespace GridSketch;

public partial class MainWindow
{
    private void ChooseParent(SceneObject node)
        => ChooseParents([node]);

    private void ChooseSelectedParents() => ChooseParents(SelectedLayers());

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
