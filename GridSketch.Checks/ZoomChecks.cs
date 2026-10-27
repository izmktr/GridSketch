using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using GridSketch;

internal static class ZoomChecks
{
    public static void Run(MainWindow window)
    {
        var viewport = (ScrollViewer)window.FindName("Viewport");
        var surface = (SceneSurface)window.FindName("Surface");
        var zoom = (Slider)window.FindName("Zoom");
        Point Center() => new((viewport.HorizontalOffset + viewport.ViewportWidth / 2) / zoom.Value - surface.OriginX,
            (viewport.VerticalOffset + viewport.ViewportHeight / 2) / zoom.Value - surface.OriginY);
        foreach (var offset in new[] { new Point(800, 700), new Point(0, 0), new Point(3000, 2000) })
        {
            zoom.Value = 1; viewport.ScrollToHorizontalOffset(offset.X); viewport.ScrollToVerticalOffset(offset.Y); window.UpdateLayout();
            var before = Center();
            foreach (double value in new[] { 2d, 0.25, 3, 0.75, 1 })
            {
                zoom.Value = value; window.UpdateLayout();
                if ((Center() - before).Length > 0.05) throw new Exception($"Zoom moved viewport center at {offset} / {value}: {Center()} != {before}");
            }
            typeof(MainWindow).GetMethod("RefreshAll", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null); window.UpdateLayout();
            if ((Center() - before).Length > 0.05) throw new Exception("Refresh lost zoom workspace margin");
        }
        Console.WriteLine("PASS: Viewport-centered zoom, min/max zoom, workspace edges and repeated layout");
    }
}
