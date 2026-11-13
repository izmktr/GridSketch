namespace GridSketch;

public partial class MainWindow
{
    private void OpenGridTextExchange(SceneObject node)
    {
        CommitPropertyEdit();
        if (activeTab is not { } tab || node.Kind is not (ObjectKind.GridText or ObjectKind.CellSelection)) return;
        bool cellStates = node.Kind == ObjectKind.CellSelection;
        var kind = node.Kind;
        string id = node.Id;
        (SceneDocument Document, SceneObject Node) Target()
        {
            if (!editorTabs.Contains(tab)) throw new InvalidOperationException("対象のファイルは閉じられています。");
            var scene = tab == activeTab ? document : tab.Document;
            if (scene.Find(id) is not { } text || text.Kind != kind) throw new InvalidOperationException("対象のオブジェクトは削除されています。");
            return (scene, text);
        }
        void Apply(string text, GridTextFormat format)
        {
            var (scene, target) = Target();
            if (!scene.Editable(target) || !scene.Shown(target)) throw new InvalidOperationException("非表示またはロック中のオブジェクトには反映できません。");
            // Check the complete input before switching tabs or changing selection.
            var grid = scene.GridFor(target) ?? throw new InvalidOperationException("対象のグリッドがありません。");
            if (cellStates) CellStateTextTransfer.Parse(target, grid, text, format);
            else GridTextTransfer.Parse(text, format, grid.Columns, grid.Rows);
            SwitchTab(tab);
            target = Target().Node;
            ChangeGridContent(() =>
            {
                if (cellStates) document.ImportCellStates(target, text, format);
                else document.ImportGridText(target, text, format);
            });
            selectedId = id; layerSelection.Clear(); layerSelection.Add(id);
            RefreshAll();
        }
        string file = tab.Owner is not null ? "部品：" + tab.Name : tab == activeTab && path is not null ? System.IO.Path.GetFileName(path) : "無題";
        new GridTextExchangeWindow(file + " ／ " + node.Name, Target, Apply, cellStates) { Owner = this }.Show();
    }
}
