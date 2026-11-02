using System.Windows;

namespace GridSketch;

public partial class MainWindow
{
    private bool propertyHelpOpen;
    private PropertyHelpWindow? propertyHelpWindow;

    private void UpdatePropertyHelp()
    {
        bool available = Selected.Kind == ObjectKind.CellSelection && SelectedLayers().Count <= 1;
        PropertyHelpToggle.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        PropertyHelpToggle.IsChecked = available && propertyHelpOpen;
        if (available && propertyHelpOpen)
        {
            if (propertyHelpWindow is null)
            {
                propertyHelpWindow = new PropertyHelpWindow { Owner = this };
                propertyHelpWindow.Closed += (_, _) =>
                {
                    propertyHelpWindow = null;
                    propertyHelpOpen = false;
                    PropertyHelpToggle.IsChecked = false;
                };
            }
            if (!propertyHelpWindow.IsVisible) propertyHelpWindow.Show();
        }
        else propertyHelpWindow?.Hide();
    }

    private void PropertyHelpClick(object sender, RoutedEventArgs e)
    {
        CommitPropertyEdit();
        propertyHelpOpen = PropertyHelpToggle.IsChecked == true;
        UpdatePropertyHelp();
    }

    private void ClosePropertyHelpClick(object sender, RoutedEventArgs e)
    {
        propertyHelpOpen = false;
        propertyHelpWindow?.Close();
        UpdatePropertyHelp();
        PropertyHelpToggle.Focus();
    }
}
