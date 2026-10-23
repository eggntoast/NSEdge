using System.Windows;
using System.Windows.Interop;
using NSEdge.App.Services;

namespace NSEdge.App;

public partial class BatterySettingsWindow : Window
{
    private readonly AppSettings settings;
    private readonly SettingsStore store;
    private readonly Action lightingChanged;
    private bool initializing = true;

    public BatterySettingsWindow(AppSettings settings, SettingsStore store, Action lightingChanged)
    {
        InitializeComponent();
        this.settings = settings;
        this.store = store;
        this.lightingChanged = lightingChanged;
        PsBatteryCheck.IsChecked = settings.ShowBatteryOnPs;
        LowBatteryCheck.IsChecked = settings.ShowLowBatteryAlerts;
        ThresholdSlider.Value = settings.LowBatteryThresholdPercent;
        ThresholdText.Text = $"{settings.LowBatteryThresholdPercent}%";
        initializing = false;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        WindowAppearance.Apply(new WindowInteropHelper(this).Handle);
        using System.Drawing.Icon? icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        if (icon is not null)
            Icon = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight(16, 16));
    }

    private void SettingChanged(object sender, RoutedEventArgs e)
    {
        if (initializing || PsBatteryCheck is null || LowBatteryCheck is null || ThresholdSlider is null)
            return;

        settings.ShowBatteryOnPs = PsBatteryCheck.IsChecked == true;
        settings.ShowLowBatteryAlerts = LowBatteryCheck.IsChecked == true;
        settings.LowBatteryThresholdPercent = (int)ThresholdSlider.Value;
        ThresholdText.Text = $"{settings.LowBatteryThresholdPercent}%";
        store.Save(settings);
    }

    private void LightingClick(object sender, RoutedEventArgs e)
    {
        new LightingSettingsWindow(settings, store, lightingChanged) { Owner = this }.ShowDialog();
    }

    private void DoneClick(object sender, RoutedEventArgs e) => Close();
}