using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NSEdge.Core;
using NSEdge.App.Services;
using System.Windows.Interop;

namespace NSEdge.App;

public partial class ColorPickerWindow : Window
{
    private bool updating = true;
    public LightbarColor SelectedColor { get; private set; }

    public ColorPickerWindow(LightbarColor initial)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowAppearance.Apply(new WindowInteropHelper(this).Handle);
        SelectedColor = initial;
        RedSlider.Value = initial.Red;
        GreenSlider.Value = initial.Green;
        BlueSlider.Value = initial.Blue;
        HexText.Text = initial.Hex;
        updating = false;
        SetPreview(initial);
    }

    private void SetPreview(LightbarColor value)
    {
        ColorPreview.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(value.Red, value.Green, value.Blue));
        RedValue.Text = value.Red.ToString();
        GreenValue.Text = value.Green.ToString();
        BlueValue.Text = value.Blue.ToString();
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void SliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (updating || HexText is null || ColorPreview is null) return;
        SelectedColor = new LightbarColor((byte)RedSlider.Value, (byte)GreenSlider.Value, (byte)BlueSlider.Value);
        updating = true;
        HexText.Text = SelectedColor.Hex;
        updating = false;
        SetPreview(SelectedColor);
    }

    private void HexChanged(object sender, TextChangedEventArgs e)
    {
        if (updating || ColorPreview is null) return;
        if (!LightbarColor.TryParse(HexText.Text, out var value))
        {
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        SelectedColor = value;
        updating = true;
        RedSlider.Value = value.Red;
        GreenSlider.Value = value.Green;
        BlueSlider.Value = value.Blue;
        updating = false;
        SetPreview(value);
    }

    private void AcceptClick(object sender, RoutedEventArgs e)
    {
        if (!LightbarColor.TryParse(HexText.Text, out var selected))
        {
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        SelectedColor = selected;
        DialogResult = true;
    }

    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
