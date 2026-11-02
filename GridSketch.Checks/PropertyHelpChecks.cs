using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using GridSketch;

internal static class PropertyHelpChecks
{
    public static void Run(MainWindow window)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        void Refresh() => typeof(MainWindow).GetMethod("RefreshAll", flags)!.Invoke(window, null);
        var scene = new SceneDocument(); var grid = SceneDocument.CreateGrid("空のグリッド"); scene.Canvas.Children.Add(grid);
        var nodes = new List<SceneObject>();
        foreach (var kind in Enum.GetValues<ObjectKind>())
        {
            var node = kind == ObjectKind.Canvas ? scene.Canvas : kind == ObjectKind.Grid ? grid : new SceneObject { Kind = kind };
            if (node != scene.Canvas && node != grid) grid.Children.Add(node);
            nodes.Add(node);
        }
        var centerBorder = new SceneObject { Kind = ObjectKind.Border, ConnectCellCenters = true }; grid.Children.Add(centerBorder); nodes.Add(centerBorder);
        Set("document", scene); Set("selectedId", scene.Canvas.Id); Refresh(); window.UpdateLayout();
        var toggle = (ToggleButton)window.FindName("PropertyHelpToggle");
        toggle.IsChecked = true; toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        var help = (PropertyHelpWindow)typeof(MainWindow).GetField("propertyHelpWindow", flags)!.GetValue(window)!;
        var before = scene.Snapshot();
        foreach (var node in nodes)
        {
            Set("selectedId", node.Id); Refresh();
            if (toggle.Visibility != Visibility.Visible || !help.IsVisible || help.Title != PropertyHelpWindow.TopicName(node) + "のヘルプ")
                throw new Exception("Missing help for " + node.Kind);
            if (help != typeof(MainWindow).GetField("propertyHelpWindow", flags)!.GetValue(window) || !window.IsEnabled)
                throw new Exception("Help opens a duplicate or modal window");
            var scroll = (ScrollViewer)help.FindName("HelpScroll");
            var content = scroll.Content;
            Refresh();
            if (!ReferenceEquals(content, scroll.Content)) throw new Exception("Refreshing an object rebuilds the current help");
            if (content is not StackPanel panel || panel.Children.OfType<Expander>().Count() < 2)
                throw new Exception("Incomplete object help: " + node.Kind);
        }
        if (scene.Snapshot() != before) throw new Exception("Help navigation mutates the project");
        help.Close();
        if (toggle.IsChecked == true || typeof(MainWindow).GetField("propertyHelpWindow", flags)!.GetValue(window) is not null)
            throw new Exception("Native close did not release help window");
        toggle.IsChecked = true; toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        var reopened = (Window)typeof(MainWindow).GetField("propertyHelpWindow", flags)!.GetValue(window)!;
        if (!reopened.IsVisible || reopened == help) throw new Exception("Help cannot reopen after native close");
        reopened.Close(); Set("savedState", scene.Snapshot());
        Console.WriteLine("PASS: All object help topics, center borders, modeless reuse, stable contents, native close/reopen and document preservation");
    }
}
