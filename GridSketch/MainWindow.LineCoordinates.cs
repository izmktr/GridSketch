using System.Windows;

namespace GridSketch;

public partial class MainWindow
{
    private void LinePointFields(SceneObject node, LineHandle handle, string name)
    {
        string unit = document.UsesGridCoordinates(node) ? "マス座標" : "px";
        var point = document.LinePoint(node, handle);
        Pair($"{name} X（{unit}）", point.X,
            v => document.SetLinePoint(node, handle, new Point(v, document.LinePoint(node, handle).Y)),
            $"{name} Y（{unit}）", point.Y,
            v => document.SetLinePoint(node, handle, new Point(document.LinePoint(node, handle).X, v)));
    }
}
