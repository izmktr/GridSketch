using System.Windows;
using System.Windows.Input;

namespace GridSketch;

public partial class MainWindow
{
    private Point? panStart;
    private Point panScroll;
    private Cursor? beforePanCursor;
    private int zoomWheelRemainder;

    private void ViewportMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (panStart is not null) { e.Handled = true; return; }
        if (e.ChangedButton != MouseButton.Middle) return;
        FinishDrag(); Surface.ReleaseMouseCapture();
        Viewport.UpdateLayout();
        panStart = e.GetPosition(Viewport);
        panScroll = new Point(Viewport.HorizontalOffset, Viewport.VerticalOffset);
        beforePanCursor = Viewport.Cursor; Viewport.Cursor = Cursors.Hand;
        Surface.HoveredEdge = null; Surface.InvalidateVisual();
        if (!Viewport.CaptureMouse()) EndPan();
        e.Handled = true;
    }
    private void PanTo(Point position)
    {
        if (panStart is not { } start) return;
        var delta = position - start;
        Viewport.ScrollToHorizontalOffset(panScroll.X - delta.X);
        Viewport.ScrollToVerticalOffset(panScroll.Y - delta.Y);
        Viewport.UpdateLayout();
    }
    private void ViewportMouseMove(object sender, MouseEventArgs e)
    {
        if (panStart is null) return;
        if (e.MiddleButton == MouseButtonState.Pressed) PanTo(e.GetPosition(Viewport));
        else EndPan();
        e.Handled = true;
    }
    private void ViewportMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (panStart is null) return;
        if (e.ChangedButton == MouseButton.Middle) { PanTo(e.GetPosition(Viewport)); EndPan(); }
        e.Handled = true;
    }
    private void EndPan()
    {
        if (panStart is null) return;
        panStart = null; Viewport.Cursor = beforePanCursor;
        if (Viewport.IsMouseCaptured) Viewport.ReleaseMouseCapture();
    }
    private void ViewportLostCapture(object sender, MouseEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, Viewport)) EndPan();
    }
    private void ViewportMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { zoomWheelRemainder = 0; return; }
        FinishDrag(); Surface.ReleaseMouseCapture(); EndPan();
        ApplyWheelZoom(e.Delta); e.Handled = true;
    }
    private void ApplyWheelZoom(int delta)
    {
        zoomWheelRemainder += delta;
        int steps = zoomWheelRemainder / Mouse.MouseWheelDeltaForOneLine;
        zoomWheelRemainder %= Mouse.MouseWheelDeltaForOneLine;
        if (steps != 0) Zoom.Value = Math.Clamp(Zoom.Value + steps * Zoom.TickFrequency, Zoom.Minimum, Zoom.Maximum);
    }
}
