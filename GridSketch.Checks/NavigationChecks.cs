using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using GridSketch;

internal static class NavigationChecks
{
    public static void Run(MainWindow window)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        void Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        var viewport = (ScrollViewer)window.FindName("Viewport"); var zoom = (Slider)window.FindName("Zoom");
        var scene = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!; var before = scene.Snapshot();
        zoom.Value = 1; viewport.ScrollToHorizontalOffset(700); viewport.ScrollToVerticalOffset(600); window.UpdateLayout();
        Set("panStart", new Point(100, 100)); Set("panScroll", new Point(700, 600));
        Call("PanTo", new Point(160, 140));
        if (viewport.HorizontalOffset != 640 || viewport.VerticalOffset != 560) throw new Exception("Middle drag did not pan with mouse movement");
        Call("EndPan"); Call("PanTo", new Point(200, 200));
        if (viewport.HorizontalOffset != 640) throw new Exception("Pan did not stop");
        Call("ApplyWheelZoom", 60); if (zoom.Value != 1) throw new Exception("Partial wheel step changed zoom");
        Call("ApplyWheelZoom", 60); if (zoom.Value != 1.25) throw new Exception("Wheel zoom failed");
        Call("ApplyWheelZoom", -240); if (zoom.Value != 0.75) throw new Exception("Reverse wheel zoom failed");
        Call("ApplyWheelZoom", 12000); if (zoom.Value != 3) throw new Exception("Maximum zoom clamp failed");
        Call("ApplyWheelZoom", -12000); if (zoom.Value != 0.25) throw new Exception("Minimum zoom clamp failed");
        if (scene.Snapshot() != before) throw new Exception("View navigation modified the project");
        zoom.Value = 1;
        Console.WriteLine("PASS: Pan direction, release, wheel increments, fractional wheel input, zoom limits and document preservation");
    }
}
