using System.Windows;
using System.Windows.Interop;
using NSEdge.App.Services;

namespace NSEdge.App;

public partial class LightingConfirmationWindow : Window
{
    public LightingConfirmationWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowAppearance.Apply(new WindowInteropHelper(this).Handle);
    }
    private void ConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;
    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
