using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GridSketch;

internal static class TabChecks
{
    public static void Run(MainWindow window, string directory)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Field(string name) => typeof(MainWindow).GetField(name, flags)!.GetValue(window);
        void Set(string name, object? value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        object? Call(string name, params object?[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        object? TabField(object tab, string name) => tab.GetType().GetField(name)!.GetValue(tab);
        SceneDocument Current() => (SceneDocument)Field("document")!;
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); }
        var first = new SceneDocument(); var group = new SceneObject { Kind = ObjectKind.Group, Name = "部品", Children = [new() { Kind = ObjectKind.Shape }] };
        first.Canvas.Children.Add(group); first.Parts.Add(LibraryPart.FromGroup(group, "見本"));
        var file = Path.Combine(directory, "tab-first.gridsketch"); first.Save(file);
        Call("OpenProject", file); var a = Field("activeTab")!;
        void CheckOpenShortcut()
        {
            var focused = Keyboard.FocusedElement;
            Check(focused is DependencyObject target && Window.GetWindow(target) == window,
                "Opening a file must leave keyboard focus in the editor");
            Check(ApplicationCommands.Open.CanExecute(null, focused), "Open command must remain available after opening a file");
            int calls = 0;
            ExecutedRoutedEventHandler handler = (_, e) =>
            {
                if (e.Command != ApplicationCommands.Open) return;
                calls++; e.Handled = true;
            };
            CommandManager.AddPreviewExecutedHandler(window, handler);
            try { ApplicationCommands.Open.Execute(null, focused); ApplicationCommands.Open.Execute(null, focused); }
            finally { CommandManager.RemovePreviewExecutedHandler(window, handler); }
            Check(calls == 2, "Consecutive open commands must route from the focused editor to the window");
        }
        CheckOpenShortcut();
        Set("selectedId", Current().Canvas.Children[0].Id); Call("RefreshAll"); Call("DuplicateLayer");
        var firstSelection = Field("selectedId");
        var zoom = (Slider)window.FindName("Zoom"); zoom.Value = 1.5;
        var viewport = (ScrollViewer)window.FindName("Viewport"); viewport.ScrollToHorizontalOffset(300); viewport.UpdateLayout();
        Call("CaptureTab"); var center = (Point)TabField(a, "Center")!;
        var second = new SceneDocument(); second.Canvas.Children.Add(new() { Kind = ObjectKind.Text, Text = "第二文書" });
        Call("AddProjectTab", second, null); var b = Field("activeTab")!;
        CheckOpenShortcut();
        Check(Current() == second && ((Stack<string>)Field("undo")!).Count == 0, "New tab has independent document and undo stack");
        Call("OpenProject", file);
        CheckOpenShortcut();
        Check(ReferenceEquals(Field("activeTab"), a) && Current().Canvas.Children.Count == 2 && Equals(Field("selectedId"), firstSelection), "Opening same path reuses tab and preserves unsaved work and selection");
        Call("CaptureTab"); var restoredCenter = (Point)TabField(a, "Center")!;
        Check(zoom.Value == 1.5 && (center - restoredCenter).Length < 2, "Tab restores zoom and viewport center");
        Call("Undo"); Check(Current().Canvas.Children.Count == 1, "First tab undo remains available");
        Call("SwitchTab", b); Check(Current().Canvas.Children[0].Text == "第二文書", "Other tab unaffected by undo");
        Call("SwitchTab", a);
        var sourceDocument = Current();
        var sourceObject = sourceDocument.Canvas.Children[0];
        Set("selectedId", sourceObject.Id); Call("RefreshAll");
        string sourceBefore = sourceDocument.Snapshot(), destinationBefore = second.Snapshot();
        window.Dispatcher.BeginInvoke(new Action(() =>
        {
            var dialog = Application.Current.Windows.OfType<ReparentDialog>().Single();
            var files = PropertyChecks.Find<ComboBox>(dialog, "複製先のファイル");
            files.SelectedItem = files.Items.OfType<ComboBoxItem>().Single(i => ReferenceEquals(i.Tag, second));
            var tree = PropertyChecks.Find<TreeView>(dialog, "複製先のレイヤー");
            ((TreeViewItem)tree.Items[0]).IsSelected = true;
            PropertyChecks.Find<Button>(dialog, "選択した親へ複製").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
        Call("ChooseDuplicateParent");
        Check(ReferenceEquals(Field("activeTab"), b) && second.Canvas.Children.Count == 2
            && second.Canvas.Children[1].Id != sourceObject.Id && second.Canvas.Children[1].Children[0].Id != sourceObject.Children[0].Id
            && sourceDocument.Snapshot() == sourceBefore, "Cross-tab duplication selects destination and preserves source with fresh subtree identities");
        Check((bool)typeof(MainWindow).GetProperty("Dirty", flags)!.GetValue(window)!, "Destination becomes unsaved after duplication");
        Call("Undo"); Check(Current().Snapshot() == destinationBefore, "Cross-tab duplication undoes in destination");
        Call("Redo"); Check(Current().Canvas.Children.Count == 2, "Cross-tab duplication redoes in destination");
        Call("Undo"); Call("SwitchTab", a);
        var originalPart = Current().Parts[0]; var originalCopyWidth = Current().Canvas.Children[0].Children[0].Width;
        Call("EditPartTab", originalPart); var partTab = Field("activeTab")!;
        Check(!ReferenceEquals(partTab, a) && Current().Canvas.Children.Count == 1, "Part opens as separate editable document");
        Current().Canvas.Children[0].Children[0].Width = 321;
        Check((bool)Call("ApplyPartEdit")!, "Part edits apply successfully");
        Check(SceneDocument.Load(file).Parts[0].Template.Children[0].Width != 321, "Applying part does not silently save owner file");
        Call("SwitchTab", a);
        Check(Current().Parts[0].Template.Children[0].Width == 321 && Current().Canvas.Children[0].Children[0].Width == originalCopyWidth,
            "Apply updates library but preserves placed copies");
        Call("Save", false);
        Check(SceneDocument.Load(file).Parts[0].Template.Children[0].Width == 321, "Parent project save persists edited part");
        Call("EditPartTab", Current().Parts[0]); Check(ReferenceEquals(Field("activeTab"), partTab), "Reopening part selects existing editor");
        Call("CloseEditorTab", partTab); Call("SwitchTab", a);
        Call("Undo"); Check(Current().Parts[0].Template.Children[0].Width == originalCopyWidth, "Library apply is undoable in owner project");
        Set("savedState", Current().Snapshot()); Call("CaptureTab");
        var dropFirst = Path.Combine(directory, "drop-first.gridsketch");
        var dropSecond = Path.Combine(directory, "drop-second.gridsketch");
        new SceneDocument().Save(dropFirst); new SceneDocument().Save(dropSecond);
        int beforeDrop = ((IList)Field("editorTabs")!).Count;
        var dropData = new DataObject(DataFormats.FileDrop, new[] { dropFirst, dropSecond, dropFirst, Path.Combine(directory, "ignored.png") });
        Call("OpenDroppedProjects", dropData);
        Check(window.AllowDrop && ((IList)Field("editorTabs")!).Count == beforeDrop + 2
            && Equals(Field("path"), dropSecond), "Dropping multiple projects must open distinct tabs and ignore unrelated files");
        var droppedScene = Current(); droppedScene.Canvas.Background = "#123456";
        Call("OpenDroppedProjects", dropData);
        Check(((IList)Field("editorTabs")!).Count == beforeDrop + 2 && ReferenceEquals(Current(), droppedScene)
            && Current().Canvas.Background == "#123456", "Repeated drops must reuse tabs without replacing unsaved content");
        CheckOpenShortcut();
        var stateData = new DataObject("GridSketch.State", "state");
        var projects = (string[])typeof(MainWindow).GetMethod("DroppedProjects", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [stateData])!;
        Check(projects.Length == 0, "Internal state dragging must not be treated as file dropping");
        Set("savedState", Current().Snapshot()); Call("CaptureTab");
        // Tests deliberately replace window documents elsewhere. Mark all test-only sessions clean before closing.
        foreach (var tab in (IEnumerable)Field("editorTabs")!) tab.GetType().GetField("Saved")!.SetValue(tab, ((SceneDocument)TabField(tab, "Document")!).Snapshot());
        int count = ((IList)Field("editorTabs")!).Count;
        Call("CloseEditorTab", b);
        Check(((IList)Field("editorTabs")!).Count == count - 1, "Closing a project removes only its tab");
        Call("SwitchTab", a); Call("CenterClick", window, new RoutedEventArgs());
        Call("UpdateTabHeaders"); window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(directory, "tabs-preview.png"))) encoder.Save(stream);
        Console.WriteLine("PASS: Independent tabs, duplicate-open handling, view and selection restoration, part editing/apply, persistence and undo");
    }
}
