using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using GridSketch;

internal static class UndoMergeChecks
{
    public static void Run(MainWindow window)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        void Call(string name) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, null);
        SceneDocument Current() => (SceneDocument)typeof(MainWindow).GetField("document", flags)!.GetValue(window)!;
        var scene = new SceneDocument(); var shape = new SceneObject { Kind = ObjectKind.Shape, Width = 100, Height = 100 };
        scene.Canvas.Children.Add(shape); Set("document", scene); Set("selectedId", shape.Id); Call("RefreshAll");
        void Edit(string name, string value)
        {
            PropertyChecks.Find<TextBox>((StackPanel)window.FindName("Properties"), name).Text = value;
            Call("CommitPropertyEdit");
        }
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        Edit("幅（px）", "120"); Edit("幅（px）", "140");
        Call("Undo"); Check(Current().Find(shape.Id)!.Width == 100, "Repeated edits to the same field merge");
        Call("Redo"); Check(Current().Find(shape.Id)!.Width == 140, "Merged redo restores the final value");
        Edit("幅（px）", "160"); Call("Undo");
        Check(Current().Find(shape.Id)!.Width == 140, "Redo separates subsequent edits");
        Edit("幅（px）", "180"); Edit("高さ（px）", "120"); Edit("幅（px）", "200");
        Call("Undo"); Check(Current().Find(shape.Id)!.Width == 180 && Current().Find(shape.Id)!.Height == 120, "Different fields split undo merging");
        void Move(double x)
        {
            Set("dragSnapshot", Current().Snapshot()); Set("dragStart", new Point(0, 0));
            Current().Find(shape.Id)!.X = x; Call("FinishDrag");
        }
        Move(10); Move(30); Call("Undo");
        Check(Current().Find(shape.Id)!.X == 0, "Repeated movement gestures merge");
        Call("Redo"); Check(Current().Find(shape.Id)!.X == 30, "Merged move redo restores the final position");
        Console.WriteLine("PASS: Consecutive property/move undo merging, different-field boundaries, undo and redo");
    }
}
