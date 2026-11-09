using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GridSketch;

internal static class LineHandleChecks
{
    public static void Run(MainWindow window)
    {
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        var scene = new SceneDocument(); var group = new SceneObject { Kind = ObjectKind.Group, X = 50, Y = 30, Angle = 30 };
        scene.Canvas.Children.Add(group);
        var line = new SceneObject { Kind = ObjectKind.Line, X = 20, Y = 40, Angle = 20, EndX = 120, EndY = 30, ControlX = 60, ControlY = -50, PathKind = "曲線" }; group.Children.Add(line);
        Point World(Point p) => scene.World(line).Transform(p);
        var end = World(new(line.EndX, line.EndY)); var control = World(new(line.ControlX, line.ControlY));
        var drag = new LineHandleDrag(scene, line, LineHandle.Start); drag.Move(new Vector(20, 10), 1); scene.Validate();
        Check((World(new(line.EndX, line.EndY)) - end).Length < 1e-7 && (World(new(line.ControlX, line.ControlY)) - control).Length < 1e-7,
            "Moving the start must keep end/control fixed under rotated ancestors");
        drag.Move(new Vector(), 1); Check(line.X == 20 && line.Y == 40 && line.Angle == 20, "Returning to drag origin restores the original transform");
        var start = World(new Point());
        drag = new LineHandleDrag(scene, line, LineHandle.End); drag.Move(new Vector(25, -12), 2); scene.Validate();
        Check((World(new Point()) - start).Length < 1e-7 && (World(new(line.ControlX, line.ControlY)) - control).Length < 1e-7, "Moving end preserves start and control");
        end = World(new(line.EndX, line.EndY));
        drag = new LineHandleDrag(scene, line, LineHandle.Control); drag.Move(new Vector(12, 20), 1);
        Check((World(new Point()) - start).Length < 1e-7 && (World(new(line.EndX, line.EndY)) - end).Length < 1e-7, "Control edit preserves endpoints");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        Set("document", scene); Set("selectedId", line.Id); Set("cellEditMode", false); Call("RefreshAll");
        var surface = (SceneSurface)window.FindName("Surface");
        ((System.Windows.Controls.Slider)window.FindName("Zoom")).Value = 2;
        Point SurfacePoint(Point p) { var world = scene.World(line).Transform(p); return new(world.X + surface.OriginX, world.Y + surface.OriginY); }
        var point = SurfacePoint(new(line.ControlX, line.ControlY));
        Check(surface.LineHandleAt(point + new Vector(4, 0)) == LineHandle.Control && surface.LineHandleAt(point + new Vector(6, 0)) is null, "Handle hit area is zoom independent");
        surface.ShowSelectionBounds = false; Check(surface.LineHandleAt(point) is null, "Cell editing hides handles"); surface.ShowSelectionBounds = true;
        line.Locked = true; Check(surface.LineHandleAt(point) is null, "Locked lines have no editable handles"); line.Locked = false;
        var before = scene.Snapshot();
        Check((bool)Call("StartLineHandle", point)!, "Control handle can start a drag");
        var liveDrag = (LineHandleDrag)typeof(MainWindow).GetField("lineHandleDrag", flags)!.GetValue(window)!; liveDrag.Move(new(20, 20), 2);
        var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        window.RaiseEvent(escape);
        var restored = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        Check(escape.Handled && restored.Snapshot() == before && !surface.IsMouseCaptured, "Escape cancels handle changes without retaining capture");
        scene = restored; line = scene.Find(line.Id)!;
        for (int i = 0; i < 2; i++)
        {
            Check((bool)Call("StartLineHandle", SurfacePoint(new(line.EndX, line.EndY)))!, "End handle drag did not start");
            ((LineHandleDrag)typeof(MainWindow).GetField("lineHandleDrag", flags)!.GetValue(window)!).Move(new(15, 5), 2);
            Call("FinishDrag"); surface.ReleaseMouseCapture();
        }
        Call("Undo"); restored = (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        Check(restored.Snapshot() == before, "Consecutive drags of the same point must undo together");
        Console.WriteLine("PASS: Line handle editing, untouched endpoints, nested transforms, zoom hit testing, locks and Escape rollback");
    }
}
