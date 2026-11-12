using System.Windows;
using GridSketch;

internal static class CenterRotationChecks
{
    public static void Run()
    {
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        foreach (var kind in new[] { ObjectKind.Shape, ObjectKind.Image, ObjectKind.Text, ObjectKind.Line, ObjectKind.Grid, ObjectKind.Group })
        foreach (var sizing in new[] { "固定", "収める", "広げる" })
        foreach (var anchor in new[] { "左上", "中央", "右下" })
        {
            var scene = new SceneDocument();
            var parent = new SceneObject { Kind = ObjectKind.Group, HasLayoutFrame = true, Width = 300, Height = 220, Angle = 21 };
            var node = new SceneObject { Kind = kind, Width = 90, Height = 40, Text = "中央", FontSize = 25,
                EndX = -70, EndY = 32, HasLayoutFrame = true, Columns = 3, Rows = 2,
                UseParentLayout = true, LayoutRegion = "親の外側", LayoutSizing = sizing, LayoutAnchor = anchor, X = 11, Y = -8 };
            scene.Canvas.Children.Add(parent); parent.Children.Add(node);
            var rect = SceneDocument.OwnLayoutRect(node);
            var center = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
            var expected = scene.World(node).Transform(center);
            var beforeSize = scene.EffectiveLocalMatrix(node).Transform(new Vector(1, 0)).Length;
            foreach (double angle in new[] { 30d, 90, 180, -45 })
            {
                node.Angle = angle;
                Check((scene.World(node).Transform(center) - expected).Length < 1e-7, $"{kind}/{sizing}/{anchor} moved its center during rotation");
                Check(Math.Abs(scene.EffectiveLocalMatrix(node).Transform(new Vector(1, 0)).Length - beforeSize) < 1e-7,
                    "Rotation must not refit or resize the object");
            }
            node.UseParentLayout = false; node.Angle = 0;
            expected = scene.World(node).Transform(center);
            node.Angle = 75;
            Check((scene.World(node).Transform(center) - expected).Length < 1e-7, "Free placement must also rotate around the object center");
        }
        var curve = new SceneObject { Kind = ObjectKind.Line, PathKind = "曲線", EndX = 100, EndY = 0, ControlX = 50, ControlY = 100 };
        var curveBounds = SceneDocument.OwnLayoutRect(curve);
        Check(Math.Abs(curveBounds.X + curveBounds.Width / 2 - 50) < 1e-7
            && Math.Abs(curveBounds.Y + curveBounds.Height / 2 - 25) < 1e-7, "Curve center must use the curve bounds, not its control point");
        Console.WriteLine("PASS: All rotatable object centers, parent alignment, fitting, curve bounds, negative endpoints and nested rotations");
    }
}
