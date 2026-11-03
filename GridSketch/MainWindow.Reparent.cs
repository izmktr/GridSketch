using System.Windows;

namespace GridSketch;

public partial class MainWindow
{
    private void ChooseParent(SceneObject node)
    {
        CommitPropertyEdit(); RequireEditable(node);
        var dialog = new ReparentDialog(document, node) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedParentId is not { } id) return;
        Change(() =>
        {
            var target = document.Find(id) ?? throw new InvalidOperationException("移動先が見つかりません。");
            document.Reparent(node, target);
        });
    }
}
