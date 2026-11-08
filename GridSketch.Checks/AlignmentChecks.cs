using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using GridSketch;

internal static class AlignmentChecks
{
    public static void Run(MainWindow window, string directory)
    {
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        var doc = new SceneDocument(); var grid = SceneDocument.CreateGrid("空のグリッド", 1, 1, 80); doc.Canvas.Children.Add(grid);
        var text = new SceneObject { Kind = ObjectKind.GridText, FontSize = 20, Glyphs = [new() { Text = "a" }] }; grid.Children.Add(text);
        string[] directions = ["左上", "上", "右上", "左", "中央", "右", "左下", "下", "右下"];
        var boxes = new List<Rect>();
        foreach (var direction in directions)
        {
            text.Alignment = direction; doc.Validate(); boxes.Add(new SceneRenderer(doc).Bounds[text.Id]);
            Check(SceneDocument.Restore(doc.Snapshot()).Find(text.Id)!.Alignment == direction, "Alignment round trip failed");
        }
        for (int i = 0; i < 9; i++)
        {
            if (i % 3 != 2) Check(boxes[i].X < boxes[i + 1].X, "Horizontal alignment order failed");
            if (i < 6) Check(boxes[i].Y < boxes[i + 3].Y, "Vertical alignment order failed");
        }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Call(string name) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, null);
        typeof(MainWindow).GetField("document", flags)!.SetValue(window, doc);
        typeof(MainWindow).GetField("selectedId", flags)!.SetValue(window, text.Id); text.Alignment = "中央"; Call("RefreshAll");
        var panel = (StackPanel)window.FindName("Properties");
        var button = PropertyChecks.Find<ToggleButton>(panel, "配置：右下");
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(text.Alignment == "右下" && ((UniformGrid)button.Parent).Children.OfType<ToggleButton>().Count(b => b.IsChecked == true) == 1, "Alignment selection is not exclusive/immediate");
        var row = (UniformGrid)button.Parent;
        panel.Children.Remove(row);
        var preview = new Window { Owner = window, Width = 160, Height = 180, Content = row, ShowInTaskbar = false };
        preview.Show(); preview.UpdateLayout();
        Check(Math.Abs(row.ActualWidth - 120) < 0.1 && row.Rows == 3 && row.Columns == 3, $"Alignment buttons do not fit compact grid: {row.ActualWidth}, {row.Rows}, {row.Columns}, {row.Visibility}");
        var bitmap = new RenderTargetBitmap(120, 120, 96, 96, PixelFormats.Pbgra32); bitmap.Render(row);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(directory, "alignment-icons.png"))) encoder.Save(stream);
        preview.Close();
        Call("Undo"); var restored = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        Check(restored.Find(text.Id)!.Alignment == "中央", "Alignment undo failed");
        var free = new SceneObject { Kind = ObjectKind.Text, UseParentLayout = true }; restored.Canvas.Children.Add(free);
        restored.Canvas.AutoSize = false;
        typeof(MainWindow).GetField("selectedId", flags)!.SetValue(window, free.Id); Call("RefreshAll");
        PropertyChecks.Find<ToggleButton>(panel, "位置合わせ：右下").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(free.LayoutAnchor == "右下", "Free text position alignment did not use the existing layout anchor");
        Console.WriteLine("PASS: Nine text alignments, persistence, compact exclusive icons, undo and free-text layout anchor");
    }
}
