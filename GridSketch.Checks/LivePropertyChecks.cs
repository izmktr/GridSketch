using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using GridSketch;

internal static class LivePropertyChecks
{
    private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    public static void Commit(MainWindow window) => typeof(MainWindow).GetMethod("CommitPropertyEdit", Flags)!.Invoke(window, null);
    public static void Run(MainWindow window)
    {
        void Call(string name) => typeof(MainWindow).GetMethod(name, Flags)!.Invoke(window, null);
        void Set(string name, object value) => typeof(MainWindow).GetField(name, Flags)!.SetValue(window, value);
        SceneDocument Current() => (SceneDocument)typeof(MainWindow).GetField("document", Flags)!.GetValue(window)!;
        var scene = new SceneDocument(); var shape = new SceneObject { Kind = ObjectKind.Shape, Width = 100, Height = 80 };
        scene.Canvas.Children.Add(shape); Set("document", scene); Set("selectedId", shape.Id); Call("RefreshAll");
        var panel = (StackPanel)window.FindName("Properties");
        if (panel.Children.OfType<Button>().Any(b => Equals(b.Content, "プロパティを適用"))) throw new Exception("Apply button remains");
        var width = PropertyChecks.Find<TextBox>(panel, "幅（px）");
        width.Text = "2"; width.Text = "25"; width.Text = "250";
        if (shape.Width != 250 || !ReferenceEquals(width, PropertyChecks.Find<TextBox>(panel, "幅（px）"))) throw new Exception("Live preview replaced editor or did not apply");
        width.Text = "-";
        if (shape.Width != 250 || width.ToolTip is null) throw new Exception("Invalid draft changed the model");
        Commit(window);
        if (width.Text != "250") throw new Exception("Invalid draft was not restored to valid value");
        Call("Undo"); if (Current().Find(shape.Id)!.Width != 100) throw new Exception("Numeric edit was not grouped into one undo");
        Call("Redo"); if (Current().Find(shape.Id)!.Width != 250) throw new Exception("Numeric edit redo failed");
        panel = (StackPanel)window.FindName("Properties");
        PropertyChecks.Find<TextBox>(panel, "幅（px）").Text = "300";
        PropertyChecks.Find<TextBox>(panel, "高さ（px）").Text = "90"; Commit(window);
        Call("Undo"); if (Current().Find(shape.Id)!.Width != 300 || Current().Find(shape.Id)!.Height != 80) throw new Exception("Different fields shared one undo");
        Call("Undo"); if (Current().Find(shape.Id)!.Width != 250) throw new Exception("Previous field undo failed");
        panel = (StackPanel)window.FindName("Properties");
        width = PropertyChecks.Find<TextBox>(panel, "幅（px）");
        width.Text = "333";
        width.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        if (Current().Find(shape.Id)!.Width != 250) throw new Exception("Escape did not cancel the field");
        panel = (StackPanel)window.FindName("Properties");
        var slider = PropertyChecks.Find<Slider>(panel, "幅（px） スライダー");
        slider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
        slider.Value = Math.Log2(48); slider.Value = Math.Log2(96);
        slider.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
        if (Current().Find(shape.Id)!.Width != 96) throw new Exception("Slider did not preview");
        Call("Undo"); if (Current().Find(shape.Id)!.Width != 250) throw new Exception("Slider gesture was split across undo entries");
        var canvas = Current().Canvas; canvas.AutoSize = false; canvas.Width = 500; canvas.Height = 400;
        Set("selectedId", canvas.Id); Call("RefreshAll");
        panel = (StackPanel)window.FindName("Properties");
        var fixedPanel = panel.Children.OfType<StackPanel>().Last();
        fixedPanel.Children.OfType<ComboBox>().Last().SelectedItem = "A4";
        if (canvas.CanvasUnit != "mm" || canvas.Width != 210 || canvas.Height != 297) throw new Exception("Live paper preset failed");
        Call("Undo");
        if (Current().Canvas.Width != 500 || Current().Canvas.Height != 400 || Current().Canvas.CanvasUnit != "px") throw new Exception("Paper preset was not one undo entry");
        var text = new SceneObject { Kind = ObjectKind.Text, Text = "開始" }; Current().Canvas.Children.Add(text);
        Set("selectedId", text.Id); Call("RefreshAll");
        var settle = new System.Windows.Threading.DispatcherFrame();
        window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => settle.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(settle);
        panel = (StackPanel)window.FindName("Properties");
        var input = panel.Children.OfType<TextBox>().Single(b => b.AcceptsReturn); input.Focus();
        Set("propertyComposing", true); input.Text = "日本";
        if (text.Text != "開始") throw new Exception("Property applied unconfirmed IME composition");
        var composition = new TextComposition(InputManager.Current, input, "日本");
        input.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = TextCompositionManager.TextInputEvent });
        var frame = new System.Windows.Threading.DispatcherFrame();
        window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        input.Text = "日本語"; Commit(window);
        if (text.Text != "日本語") throw new Exception("Property text did not preview after composition");
        Call("Undo"); if (Current().Find(text.Id)!.Text != "開始") throw new Exception("Text editing was split across undo entries: " + Current().Find(text.Id)!.Text);
        Set("savedState", Current().Snapshot());
        Console.WriteLine("PASS: Live numeric preview, stable editors, validation recovery and per-field undo/redo");
    }
}
