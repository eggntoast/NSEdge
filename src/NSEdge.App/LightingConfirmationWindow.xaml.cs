using System.Windows;

namespace NSEdge.App;

public partial class LightingConfirmationWindow : Window
{
    public LightingConfirmationWindow() => InitializeComponent();
    private void ConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;
    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
