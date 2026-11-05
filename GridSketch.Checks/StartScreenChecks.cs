using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GridSketch;

internal static class StartScreenChecks
{
    public static void Run(MainWindow window, string directory)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Field(string name) => typeof(MainWindow).GetField(name, flags)!.GetValue(window);
        object? Call(string name, params object?[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        var tabs = (IList)Field("editorTabs")!;
        var start = (FrameworkElement)window.FindName("StartScreen"); var editor = (FrameworkElement)window.FindName("EditorArea");
        Check(tabs.Count == 0 && Field("activeTab") is null && start.IsVisible && !editor.IsVisible, "Startup has no document and displays start screen");
        Check(!ApplicationCommands.Save.CanExecute(null, window) && !((MenuItem)window.FindName("AddDocumentMenu")).IsEnabled,
            "Document commands disabled on start screen");
        window.UpdateLayout();
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using (var output = File.Create(Path.Combine(directory, "start-screen.png"))) encoder.Save(output);
        var scene = new SceneDocument(); scene.Canvas.Children.Add(SceneDocument.CreateGrid("基本の方眼紙"));
        Call("AddProjectTab", scene, null);
        Check(tabs.Count == 1 && editor.IsVisible && !start.IsVisible, "First project opens without a phantom tab");
        Check((bool)Call("CloseEditorTab", Field("activeTab"))!, "Clean last tab closes");
        Check(tabs.Count == 0 && start.IsVisible && !editor.IsVisible && (bool)Call("CanCloseAllTabs")!, "Closing last tab restores start screen without a save prompt");
        var file = Path.Combine(directory, "start-project.gridsketch"); scene.Save(file);
        Call("OpenProject", file); Call("OpenProject", file);
        Check(tabs.Count == 1 && editor.IsVisible, "Opening from empty state reuses existing paths");
        Call("CloseEditorTab", Field("activeTab"));
        Call("AddProjectTab", scene, null);
        Console.WriteLine("PASS: Empty startup, start screen, command guards, first/open/last tab lifecycle");
    }
}
