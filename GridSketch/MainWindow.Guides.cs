using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GridSketch;

public partial class MainWindow
{
    private void GuideModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Surface is null || sender is not ComboBox box) return;
        Surface.GuideMode = (box.SelectedItem as ComboBoxItem)?.Content as string ?? "自動";
        Surface.InvalidateVisual();
    }
    private void SetCleanPreview(bool active)
    {
        Surface.CleanPreview = active; GridInput.Opacity = active ? 0 : 1; Surface.InvalidateVisual();
    }
    private void GuidePreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F8 || activeTab is null) return;
        SetCleanPreview(true); e.Handled = true;
    }
    private void GuidePreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F8) return;
        SetCleanPreview(false); e.Handled = true;
    }
}
