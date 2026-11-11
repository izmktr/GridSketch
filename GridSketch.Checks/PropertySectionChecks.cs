using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GridSketch;

internal static class PropertySectionChecks
{
    public static void Run(MainWindow window, string directory)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string key, object value) => typeof(MainWindow).GetField(key, flags)!.SetValue(window, value);
        void Refresh() => typeof(MainWindow).GetMethod("RefreshAll", flags)!.Invoke(window, null);
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        var stateFile = Path.Combine(directory, Guid.NewGuid() + "-section-state.json");
        Set("propertySectionState", new PropertySectionState(stateFile));
        var doc = new SceneDocument();
        var first = new SceneObject { Kind = ObjectKind.Text, Text = "文章をここで編集" };
        var second = new SceneObject { Kind = ObjectKind.Text };
        doc.Canvas.Children.AddRange([first, second]);
        Set("document", doc); Set("selectedId", first.Id); Refresh();
        var panel = (StackPanel)window.FindName("Properties");
        Check(panel.Children.OfType<TextBox>().Any(b => b.AcceptsReturn), "Free text body must stay outside collapsed sections");
        Check(PropertyChecks.Section(panel, "文字").Visibility == Visibility.Visible
            && PropertyChecks.Section(panel, "縁取り").Visibility == Visibility.Collapsed, "Section defaults should prioritize common settings");
        string snapshot = doc.Snapshot();
        var toggle = PropertyChecks.Find<ToggleButton>(panel, "文字の開閉");
        toggle.IsChecked = false; toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(PropertyChecks.Section(panel, "文字").Visibility == Visibility.Collapsed && doc.Snapshot() == snapshot,
            "Collapsing must hide controls without changing document content");
        Set("selectedId", second.Id); Refresh();
        Check(PropertyChecks.Section(panel, "文字").Visibility == Visibility.Collapsed, "Section preference must follow object type");
        Set("propertySectionState", new PropertySectionState(stateFile)); Refresh();
        Check(PropertyChecks.Section(panel, "文字").Visibility == Visibility.Collapsed, "Section preference must persist on disk");
        var grid = SceneDocument.CreateGrid("空のグリッド"); doc.Canvas.Children.Add(grid);
        var frame = new SceneObject { Kind = ObjectKind.CellFrame, Name = "領域枠" }; grid.Children.Add(frame);
        Set("selectedId", frame.Id); Refresh(); window.UpdateLayout();
        Check(PropertyChecks.Section(panel, "外周線").Visibility == Visibility.Visible
            && PropertyChecks.Section(panel, "内部線").Visibility == Visibility.Visible, "Region line sections must start open");
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(directory, "property-sections.png"))) png.Save(file);
        Set("savedState", doc.Snapshot());
        typeof(MainWindow).GetMethod("CaptureTab", flags)!.Invoke(window, null);
        Console.WriteLine("PASS: Property sections, initial visibility, type-based persistence and document independence");
    }
}
