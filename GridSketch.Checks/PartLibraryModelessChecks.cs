using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using GridSketch;

internal static class PartLibraryModelessChecks
{
    public static void Run()
    {
        var window = new MainWindow(); window.Show();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Field(string name) => typeof(MainWindow).GetField(name, flags)!.GetValue(window);
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        void Call(string name, params object?[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        var first = new SceneDocument();
        var parent = new SceneObject { Kind = ObjectKind.Group, OrganizeOnly = true, Name = "追加先" }; first.Canvas.Children.Add(parent);
        first.Parts.Add(LibraryPart.FromGroup(new SceneObject { Kind = ObjectKind.Group, Children = [new() { Kind = ObjectKind.Shape }] }, "部品A"));
        Call("AddProjectTab", first, null);
        Call("PartLibraryClick", window, new RoutedEventArgs());
        var library = (Window)Field("partLibraryWindow")!; library.UpdateLayout();
        var layout = (DockPanel)library.Content;
        var list = layout.Children.OfType<ListBox>().Single();
        var actions = layout.Children.OfType<WrapPanel>().Single();
        Button Button(string label) => actions.Children.OfType<Button>().Single(b => Equals(b.Content, label));
        void Click(string label) => Button(label).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Check(library.IsVisible && !ComponentDispatcher.IsThreadModal, "Library must open modelessly through the actual menu handler");
        Call("PartLibraryClick", window, new RoutedEventArgs());
        Check(ReferenceEquals(library, Field("partLibraryWindow")), "Repeated opening must reuse the library window");
        Set("selectedId", parent.Id); Call("RefreshAll"); window.UpdateLayout();
        Check(layout.Children.OfType<TextBlock>().Single().Text.Contains("追加先：追加先"), "Library target follows selection in the main window");
        Click("パーツを配置"); Click("パーツを配置");
        Check(parent.Children.Count == 2 && parent.Children.All(c => c.Children.Count == 1), "Repeated insertion stays in the selected parent instead of nesting copies");
        Call("Undo"); first = (SceneDocument)Field("document")!;
        Check(first.Find(parent.Id)!.Children.Count == 1, "Main-window undo remains usable while the library is open");
        Call("Change", new Action(() => first.Parts.Add(LibraryPart.FromGroup(new SceneObject { Kind = ObjectKind.Group }, "追加部品"))));
        Check(list.Items.Count == 2, "Library reflects registrations made in the main window");
        Call("Undo"); first = (SceneDocument)Field("document")!;
        Check(list.Items.Count == 1, "Library follows undo and document replacement");
        Set("savedState", first.Snapshot());
        var second = new SceneDocument();
        second.Parts.Add(LibraryPart.FromGroup(new SceneObject { Kind = ObjectKind.Group }, "部品B"));
        Call("AddProjectTab", second, null);
        Check(list.Items.Count == 1 && Equals(((ListBoxItem)list.Items[0]).Tag, second.Parts[0].Id), "Library follows the active tab without retaining stale parts");
        Click("パーツを配置");
        Check(second.Canvas.Children.Single().Name == "部品B" && first.Find(parent.Id)!.Children.Count == 1, "Insertion affects only the current tab");
        Set("savedState", second.Snapshot()); Call("CloseTabClick", window, new RoutedEventArgs());
        Check(Equals(((ListBoxItem)list.Items[0]).Tag, first.Parts[0].Id), "Closing a tab returns the library to the remaining project");
        Set("savedState", ((SceneDocument)Field("document")!).Snapshot()); Call("CloseTabClick", window, new RoutedEventArgs());
        Check(library.IsVisible && list.Items.Count == 0 && !Button("パーツを配置").IsEnabled && !Button("インポート…").IsEnabled,
            "Closing the last project clears and disables library actions without stale targets");
        library.Close(); Check(Field("partLibraryWindow") is null, "Closed library releases its refresh callback");
        Call("AddProjectTab", new SceneDocument(), null); Call("PartLibraryClick", window, new RoutedEventArgs());
        var reopened = (Window)Field("partLibraryWindow")!;
        Check(reopened.IsVisible && !ReferenceEquals(reopened, library), "Library reopens after native close");
        window.Close(); Check(!reopened.IsVisible, "Closing the main window also closes the library");
        Console.WriteLine("PASS: Modeless library, window reuse, live selection, repeat insertion, undo, tab switching, registration refresh and window lifecycle");
    }
}
