using System.Windows;
using System.Windows.Input;

namespace GridSketch;

public partial class MainWindow
{
    private LineHandleDrag? lineHandleDrag;
    private bool StartLineHandle(Point point)
    {
        if (Surface.LineHandleAt(point) is not { } handle) return false;
        CommitPropertyEdit();
        dragSnapshot = document.Snapshot(); dragStart = Surface.ToWorld(point);
        lineHandleDrag = new LineHandleDrag(document, Selected, handle);
        Surface.HoveredLineHandle = handle; Surface.Cursor = Cursors.SizeAll;
        Surface.CaptureMouse(); return true;
    }
    private void CancelLineHandle(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || lineHandleDrag is null || dragSnapshot is not { } snapshot) return;
        lineHandleDrag = null; dragStart = null; dragSnapshot = null;
        document = SceneDocument.Restore(snapshot); Surface.ReleaseMouseCapture();
        Surface.HoveredLineHandle = null; Surface.Cursor = null; RefreshAll(); e.Handled = true;
    }
}
