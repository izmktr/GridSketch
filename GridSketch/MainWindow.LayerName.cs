using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace GridSketch;

public partial class MainWindow
{
    private void BeginLayerNameEdit(TreeViewItem item)
    {
        if (SelectedLayers().Count != 1 || !Equals(item.Tag, selectedId) || !document.Editable(Selected)
            || item.Header is TextBox) return;
        CommitPropertyEdit(); BreakUndoMerge();
        var scene = document;
        var node = Selected;
        var editor = new TextBox { Text = node.Name, MaxLength = 200, MinWidth = 120,
            Padding = new Thickness(2), Margin = new Thickness(0) };
        AutomationProperties.SetName(editor, "レイヤー名");
        bool finished = false;
        void Finish(bool commit)
        {
            if (finished) return;
            finished = true;
            string name = editor.Text.Trim();
            item.Header = node.Label;
            if (!commit || refreshing || document != scene || scene.Find(node.Id) != node
                || !scene.Editable(node) || name.Length == 0 || name == node.Name) return;
            ChangeGridContent(() => node.Name = name);
            item.Header = node.Label;
            BuildProperties();
        }
        editor.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Escape)) return;
            e.Handled = true;
            Finish(e.Key == Key.Enter);
            item.Focus();
        };
        editor.LostKeyboardFocus += (_, _) => Finish(true);
        item.Header = editor;
        editor.Focus(); editor.SelectAll();
    }
}
