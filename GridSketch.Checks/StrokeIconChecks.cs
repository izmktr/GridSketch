using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GridSketch;

internal static class StrokeIconChecks
{
    public static void Run(MainWindow window, string directory)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        void Call(string name) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, null);
        var scene = new SceneDocument(); var grid = SceneDocument.CreateGrid("基本の方眼紙"); scene.Canvas.Children.Add(grid);
        var border = grid.Children.First(n => n.Kind == ObjectKind.Border);
        Set("document", scene); Set("selectedId", border.Id); Call("RefreshAll"); window.UpdateLayout();
        var panel = (StackPanel)window.FindName("Properties");
        ToggleButton Find(string name) => PropertyChecks.Find<ToggleButton>(panel, name);
        var square = Find("端・角の形：四角"); var round = Find("端・角の形：丸"); var dotted = Find("線種：点線");
        if (square.IsChecked != true || square.Width / square.Height != 1.5) throw new Exception("Stroke icon defaults or ratio failed");
        round.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); dotted.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        round.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if (round.IsChecked != true || square.IsChecked == true || border.BorderEndCap != "丸") throw new Exception("Exclusive draft selection failed");
        window.UpdateLayout();
        var rows = PropertyChecks.Section(panel, "線の見た目").Children.OfType<WrapPanel>().Where(p => p.Children.OfType<ToggleButton>().Any()).ToList();
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 248, 112));
            for (int i = 0; i < 2; i++) dc.DrawRectangle(new VisualBrush(rows[i]) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left }, null, new Rect(4, 4 + i * 54, rows[i].ActualWidth, rows[i].ActualHeight));
        }
        var png = new RenderTargetBitmap(248, 112, 96, 96, PixelFormats.Pbgra32); png.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(png));
        using (var file = File.Create(Path.Combine(directory, "stroke-icons.png"))) encoder.Save(file);
        LivePropertyChecks.Commit(window);
        if (border.EffectiveBorderEndCap != "丸" || border.LineStyle != "点線") throw new Exception("Stroke icons failed to apply");
        Call("Undo"); Call("Undo"); var restored = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        if (restored.Find(border.Id)!.EffectiveBorderEndCap != "四角") throw new Exception("Stroke icon undo failed");
        Set("savedState", restored.Snapshot());
        Console.WriteLine("PASS: Stroke icon ratio, exclusive selection, draft/apply and undo");
    }
}
