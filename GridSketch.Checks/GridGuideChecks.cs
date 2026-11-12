using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GridSketch;

internal static class GridGuideChecks
{
    public static void Run(MainWindow window, string directory)
    {
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        var doc = new SceneDocument(); var grid = SceneDocument.CreateGrid("空のグリッド", 4, 3, 40);
        doc.Canvas.Children.Add(grid);
        var border = new SceneObject { Kind = ObjectKind.Border, ConnectCellCenters = true }; grid.Children.Add(border);
        var surface = new SceneSurface { Selected = border, ShowSelectionBounds = false }; surface.Refresh(doc);
        string snapshot = doc.Snapshot();
        Check(surface.GuideGrids().SequenceEqual([grid]), "Automatic guides must show an empty grid during cell editing");
        surface.ShowSelectionBounds = true; Check(!surface.GuideGrids().Any(), "Automatic guides must hide in move mode");
        surface.GuideMode = "常に表示"; Check(surface.GuideGrids().Count() == 1, "Always mode must work in move mode");
        grid.Visible = false; Check(!surface.GuideGrids().Any(), "Hidden grids must not show guides"); grid.Visible = true;
        surface.GuideMode = "非表示"; Check(!surface.GuideGrids().Any(), "Off mode must hide guides");
        surface.GuideMode = "自動"; surface.ShowSelectionBounds = false;
        border.Locked = true; Check(!surface.GuideGrids().Any(), "Locked objects must not show automatic editing guides"); border.Locked = false;
        var export = surface.Renderer!.Export();
        byte[] Pixels(BitmapSource bitmap)
        {
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); return pixels;
        }
        surface.CleanPreview = true; Check(!surface.GuideGrids().Any(), "Clean preview must suppress guides");
        Check(Pixels(export).SequenceEqual(Pixels(surface.Renderer.Export())) && doc.Snapshot() == snapshot,
            "Guides and preview must not change exports or document state");
        surface.CleanPreview = false;
        var viewport = new System.Windows.Controls.ScrollViewer { Content = surface, HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto };
        var host = new Window { Width = 500, Height = 420, Content = viewport, Owner = window, ShowInTaskbar = false };
        host.Show(); host.UpdateLayout();
        viewport.ScrollToHorizontalOffset(surface.OriginX - 20); viewport.ScrollToVerticalOffset(surface.OriginY - 20); host.UpdateLayout();
        var png = new RenderTargetBitmap(220, 180, 96, 96, PixelFormats.Pbgra32); png.Render(viewport);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(png));
        using (var file = File.Create(Path.Combine(directory, "grid-guide.png"))) encoder.Save(file);
        host.Close();
        var method = typeof(MainWindow).GetMethod("SetCleanPreview", BindingFlags.Instance | BindingFlags.NonPublic)!;
        method.Invoke(window, [true]);
        var live = (SceneSurface)window.FindName("Surface");
        Check(live.CleanPreview && ((UIElement)window.FindName("GridInput")).Opacity == 0, "Preview must hide the text editor as well as overlays");
        method.Invoke(window, [false]); Check(!live.CleanPreview, "Releasing preview must restore editing overlays");
        Console.WriteLine("PASS: Automatic/always/off grid guides, hidden/locked objects, clean preview and unchanged exports");
    }
}
