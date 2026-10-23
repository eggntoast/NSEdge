using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using NSEdge.App.Services;
using NSEdge.Core;

namespace NSEdge.App;

public partial class LightingSettingsWindow : Window
{
    private readonly AppSettings settings;
    private readonly SettingsStore store;
    private readonly Action onChanged;

    public LightingSettingsWindow(AppSettings settings, SettingsStore store, Action onChanged)
    {
        InitializeComponent();
        this.settings = settings;
        this.store = store;
        this.onChanged = onChanged;
        EnabledToggle.IsChecked = settings.NativeLightingEnabled;
        ModeSelector.SelectedIndex = (int)settings.LightingMode;
        CriticalText.Text = settings.LightbarCritical;
        LowText.Text = settings.LightbarLow;
        MediumText.Text = settings.LightbarMedium;
        HighText.Text = settings.LightbarHigh;
        ChargingText.Text = settings.LightbarCharging;
        FullText.Text = settings.LightbarFull;
        StaticText.Text = settings.LightbarStatic;
        CriticalBelowText.Text = settings.LightbarCriticalBelow.ToString();
        LowBelowText.Text = settings.LightbarLowBelow.ToString();
        MediumBelowText.Text = settings.LightbarMediumBelow.ToString();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        WindowAppearance.Apply(new WindowInteropHelper(this).Handle);
        using System.Drawing.Icon? icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        if (icon is not null)
            Icon = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight(16, 16));
    }

    private void ApplyClick(object sender, RoutedEventArgs e)
    {
        TextBox[] inputs = { CriticalText, LowText, MediumText, HighText, ChargingText, FullText, StaticText };
        if (inputs.Any(t => !LightbarColor.TryParse(t.Text, out _)) ||
            !int.TryParse(CriticalBelowText.Text, out int critical) ||
            !int.TryParse(LowBelowText.Text, out int low) ||
            !int.TryParse(MediumBelowText.Text, out int medium) ||
            critical < 10 || critical >= low || low >= medium || medium > 100)
        {
            MessageBox.Show(this, "Enter valid #RRGGBB colours and increasing thresholds between 10 and 100.",
                "Lighting settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        bool enabled = EnabledToggle.IsChecked == true;
        if (enabled && !settings.NativeLightingEnabled &&
            MessageBox.Show(this,
                "Enable experimental native lighting? NSEdge sends Sony lightbar reports only when DS4Windows and known controller managers are not detected. Other software may still contend for the device. Colours may persist until reconnect. The Windows output path has not been hardware-validated.",
                "Enable native lighting", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        settings.NativeLightingEnabled = enabled;
        settings.LightingMode = (LightbarMode)ModeSelector.SelectedIndex;
        settings.LightbarCritical = CriticalText.Text.ToUpperInvariant();
        settings.LightbarLow = LowText.Text.ToUpperInvariant();
        settings.LightbarMedium = MediumText.Text.ToUpperInvariant();
        settings.LightbarHigh = HighText.Text.ToUpperInvariant();
        settings.LightbarCharging = ChargingText.Text.ToUpperInvariant();
        settings.LightbarFull = FullText.Text.ToUpperInvariant();
        settings.LightbarStatic = StaticText.Text.ToUpperInvariant();
        settings.LightbarCriticalBelow = critical;
        settings.LightbarLowBelow = low;
        settings.LightbarMediumBelow = medium;
        store.Save(settings);
        onChanged();
        Close();
    }

    private void CancelClick(object sender, RoutedEventArgs e) => Close();
}
