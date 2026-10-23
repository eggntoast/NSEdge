using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using NSEdge.App.Services;
using NSEdge.Core;

namespace NSEdge.App;

public partial class LightingSettingsWindow : Window
{
    private readonly AppSettings settings;
    private readonly SettingsStore store;
    private readonly Action onChanged;
    private readonly Func<string> lightingStatus;

    public LightingSettingsWindow(AppSettings settings, SettingsStore store,
        Action onChanged, Func<string> lightingStatus)
    {
        InitializeComponent();
        this.settings = settings;
        this.store = store;
        this.onChanged = onChanged;
        this.lightingStatus = lightingStatus;
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
        LightingStatusText.Text = LightingStatusCopy.Describe(settings.NativeLightingEnabled, lightingStatus());
        RefreshSwatches();
        UpdateModeVisibility();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height - 40);
        WindowAppearance.Apply(new WindowInteropHelper(this).Handle);
        using System.Drawing.Icon? icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        if (icon is not null)
            Icon = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight(16, 16));
    }

    private void ModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BatteryPalettePanel is not null)
            UpdateModeVisibility();
    }

    private void UpdateModeVisibility()
    {
        bool isStatic = ModeSelector.SelectedIndex == (int)LightbarMode.Static;
        BatteryPalettePanel.Visibility = isStatic ? Visibility.Collapsed : Visibility.Visible;
        StaticPalettePanel.Visibility = isStatic ? Visibility.Visible : Visibility.Collapsed;
    }

    private Dictionary<string, (TextBox Text, Border Swatch)> ColorControls() => new()
    {
        ["Critical"] = (CriticalText, CriticalSwatch),
        ["Low"] = (LowText, LowSwatch),
        ["Medium"] = (MediumText, MediumSwatch),
        ["High"] = (HighText, HighSwatch),
        ["Charging"] = (ChargingText, ChargingSwatch),
        ["Full"] = (FullText, FullSwatch),
        ["Static"] = (StaticText, StaticSwatch)
    };

    private void RefreshSwatches()
    {
        foreach (var (_, (input, swatch)) in ColorControls())
            if (LightbarColor.TryParse(input.Text, out var parsed))
                swatch.Background = new SolidColorBrush(Color.FromRgb(parsed.Red, parsed.Green, parsed.Blue));
    }

    private void PickColorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key } || !ColorControls().TryGetValue(key, out var item))
            return;
        if (!LightbarColor.TryParse(item.Text.Text, out var original))
        {
            ShowValidation("Enter a valid colour, such as #75B8FF, in Advanced settings.");
            return;
        }

        var picker = new ColorPickerWindow(original) { Owner = this };
        if (picker.ShowDialog() != true) return;
        item.Text.Text = picker.SelectedColor.Hex;
        RefreshSwatches();
        ValidationText.Visibility = Visibility.Collapsed;
    }

    private void AdvancedClick(object sender, RoutedEventArgs e)
    {
        bool show = AdvancedPanel.Visibility != Visibility.Visible;
        AdvancedPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        AdvancedButton.Content = show ? "Hide advanced settings" : "Advanced settings";
        if (show)
            AdvancedPanel.BringIntoView();
    }

    private void ShowValidation(string value)
    {
        ValidationText.Text = value;
        ValidationText.Visibility = Visibility.Visible;
        ValidationText.BringIntoView();
    }

    public void AssertDarkSelector()
    {
        if (ModeSelector.Style != FindResource("DarkComboBoxStyle"))
            throw new InvalidOperationException("The dark dropdown style was not applied.");
        ModeSelector.ApplyTemplate();
        if (ModeSelector.Template.FindName("PART_Popup", ModeSelector) is not System.Windows.Controls.Primitives.Popup popup)
            throw new InvalidOperationException("Lighting mode dropdown is missing its popup.");
        if (popup.Child is not Border surface ||
            surface.Background is not SolidColorBrush background || background.Color.R > 90)
            throw new InvalidOperationException("Lighting dropdown background is not dark.");
        foreach (ComboBoxItem item in ModeSelector.Items)
        {
            if (item.Foreground is not SolidColorBrush foreground ||
                foreground.Color.R < 180 || foreground.Color.G < 180)
                throw new InvalidOperationException("Lighting dropdown item text is unreadable.");
        }
    }

    private void ApplyClick(object sender, RoutedEventArgs e)
    {
        TextBox[] inputs = { CriticalText, LowText, MediumText, HighText, ChargingText, FullText, StaticText };
        if (inputs.Any(t => !LightbarColor.TryParse(t.Text, out _)))
        {
            ShowValidation("Enter colours in #RRGGBB format. You can also choose them with the colour buttons.");
            return;
        }

        if (!int.TryParse(CriticalBelowText.Text, out int critical) ||
            !int.TryParse(LowBelowText.Text, out int low) ||
            !int.TryParse(MediumBelowText.Text, out int medium) ||
            critical < 10 || critical >= low || low >= medium || medium > 100)
        {
            ShowValidation("Battery ranges must increase, between 10% and 100%.");
            return;
        }

        bool enabled = EnabledToggle.IsChecked == true;
        if (enabled && !settings.NativeLightingEnabled)
        {
            var confirm = new LightingConfirmationWindow { Owner = this };
            if (confirm.ShowDialog() != true)
                return;
        }

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
