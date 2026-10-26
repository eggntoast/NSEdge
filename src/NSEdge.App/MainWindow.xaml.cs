using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Control = System.Windows.Controls.Control;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using NSEdge.Core;
using NSEdge.App.Services;
using NSEdge.Windows.Hid;

namespace NSEdge.App;

public partial class MainWindow : Window
{
    private readonly AppSettings settings;
    private readonly SettingsStore store;
    private readonly Action showCurrent;
    private readonly Action refresh;
    private readonly Action lightingChanged;
    private readonly Func<string> lightingStatus;
    private readonly Action showDiagnostics;
    private readonly Action exit;
    private ContextMenu? gearMenu;
    private MenuItem? autostartItem;
    private bool initializing = true;
    private bool restoringAutostart;

    public MainWindow(AppSettings settings, SettingsStore store, Action showCurrent, Action refresh, Action lightingChanged, Func<string> lightingStatus, Action showDiagnostics, Action exit)
    {
        InitializeComponent();
        this.settings = settings;
        this.store = store;
        this.showCurrent = showCurrent;
        this.refresh = refresh;
        this.lightingChanged = lightingChanged;
        this.lightingStatus = lightingStatus;
        this.showDiagnostics = showDiagnostics;
        this.exit = exit;
        CreateGearMenu();
        NotificationsCheck.IsChecked = settings.ShowNotifications;
        DurationSlider.Value = settings.NotificationSeconds;
        DurationText.Text = $"{settings.NotificationSeconds:0.0} s";
        initializing = false;
        UpdateLightingStatus();
    }

    public void SetStatus(EdgeStatus status, bool inputFresh = true)
    {
        bool connected = status.Connected;
        bool stale = connected && !inputFresh;
        bool unavailable = !connected &&
            (status.Detail.Contains("unavailable", StringComparison.OrdinalIgnoreCase) ||
             status.Detail.Contains("unsupported", StringComparison.OrdinalIgnoreCase) ||
             status.Detail.Contains("not found", StringComparison.OrdinalIgnoreCase));
        ConnectionBadgeText.Text = stale ? "Input stale" : connected ? "Connected" : unavailable ? "Unavailable" : "Searching";
        StatusDot.Fill = (System.Windows.Media.Brush)FindResource(connected && !stale ? "OnlineBrush" : "OfflineBrush");
        ConnectionBadgeText.Foreground = (System.Windows.Media.Brush)FindResource(connected && !stale ? "OnlineBrush" : "SecondaryTextBrush");
        ConnectionText.Text = stale ? "Controller connected · Input not recently verified"
            : connected ? status.Transport == EdgeTransport.Bluetooth ? "DualSense Edge  ·  Bluetooth" : "DualSense Edge  ·  USB"
            : status.Detail;
        ProfileText.ToolTip = status.NamesFromUsbCache
            ? "Saved Sony profile name from the last USB connection."
            : null;
        ProfileText.Text = stale ? "Profile unavailable"
            : status.Profile == EdgeProfile.Unknown
            ? connected && status.Transport == EdgeTransport.Bluetooth
                ? "Profile unavailable"
                : connected ? "Waiting for input" : "No active profile"
            : status.Names.DisplayName(status.Profile);
        ShortcutText.Text = stale || status.Profile == EdgeProfile.Unknown ? "—" : EdgeProfileDecoder.Shortcut(status.Profile);
        BatteryText.Text = status.Connected && !stale
            ? "Battery: " + status.Battery.Description
            : "Battery unavailable";
    }

    public void UpdateLightingStatus()
    {
        string status = lightingStatus();
        LightingModeText.Text = settings.NativeLightingEnabled ? settings.LightingMode switch
        {
            LightbarMode.BatteryBands => "Battery level",
            LightbarMode.BatteryGradient => "Battery gradient",
            LightbarMode.Static => "Single colour",
            _ => "Lighting"
        } : "Off";

        LightingStatusText.Text = LightingStatusCopy.Describe(settings.NativeLightingEnabled, status);
        LightbarPalette? palette = settings.LightingPalette();
        LightbarColor? preview = settings.NativeLightingEnabled && palette is not null
            ? LightbarPolicy.Select((System.Windows.Application.Current as App)?.CurrentBattery ?? EdgeBattery.Unavailable,
                settings.LightingMode, palette)
            : null;
        LightingPreviewDot.Fill = preview is LightbarColor c
            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(c.Red, c.Green, c.Blue))
            : (System.Windows.Media.Brush)FindResource("BorderBrush");
        LightingPreviewDot.ToolTip = preview is null ? "No lighting preview" : "Selected colour preview, not a hardware confirmation";
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var area = SystemParameters.WorkArea;
        MaxHeight = Math.Max(260, area.Height - 32);
        MaxWidth = Math.Max(320, area.Width - 32);
        Width = Math.Min(520, MaxWidth);

        using System.Drawing.Icon? icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        if (icon is not null)
            Icon = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight(16, 16));

        WindowAppearance.Apply(new WindowInteropHelper(this).Handle);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Top + (area.Height - ActualHeight) / 2;
    }

    public void SavePreview(string path)
    {
        UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(ContentViewport);
        int width = (int)Math.Ceiling(ContentViewport.ActualWidth * dpi.DpiScaleX);
        int height = (int)Math.Ceiling(ContentViewport.ActualHeight * dpi.DpiScaleY);
        if (width < 300 || height < 250)
            throw new InvalidOperationException("Invalid measured size for the WPF preview.");

        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            width, height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        bitmap.Render(ContentViewport);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var output = File.Create(path);
        encoder.Save(output);
    }

    public void AssertNormalLayout()
    {
        UpdateLayout();
        if (ResizeMode != ResizeMode.NoResize || SizeToContent != SizeToContent.Height)
            throw new InvalidOperationException("Settings window sizing is not fixed to measured content.");
        if (SettingsGearButton.ActualWidth < 30 || ContentLayout.ActualWidth < 300 ||
            gearMenu is null || autostartItem is null || gearMenu.Items.Count != 8 ||
            gearMenu.Style != FindResource("EdgeOptionsMenuStyle") ||
            !menuStyleApplied())
            throw new InvalidOperationException("Compact options menu has not been initialized.");
        bool menuStyleApplied() => (Style)gearMenu.Resources[typeof(MenuItem)] ==
            FindResource("EdgeOptionsMenuItemStyle");
        // Compact desktops may scroll; the battery options must stay reachable.
        ContentViewport.ScrollToEnd();
        UpdateLayout();
        System.Windows.Point batteryEnd = BatterySection.TranslatePoint(
            new System.Windows.Point(0, BatterySection.ActualHeight), ContentViewport);
        if (batteryEnd.Y > ContentViewport.ActualHeight + 1)
            throw new InvalidOperationException("Battery settings are not reachable.");
        ContentViewport.ScrollToTop();
        UpdateLayout();
        if (Icon is null)
            throw new InvalidOperationException("The native application icon was not initialized.");
    }

    private void SettingChanged(object sender, RoutedEventArgs e)
    {
        if (initializing || DurationSlider is null || NotificationsCheck is null)
            return;
        settings.ShowNotifications = NotificationsCheck.IsChecked == true;
        settings.NotificationSeconds = DurationSlider.Value;
        DurationText.Text = $"{settings.NotificationSeconds:0.0} s";
        store.Save(settings);
    }

    private void CreateGearMenu()
    {
        var menu = new ContextMenu
        {
            Style = (Style)FindResource("EdgeOptionsMenuStyle"),
            Placement = PlacementMode.Bottom,
            PlacementTarget = SettingsGearButton,
            HorizontalOffset = -190
        };
        menu.Resources.Add(typeof(MenuItem), FindResource("EdgeOptionsMenuItemStyle"));
        menu.Resources.Add(typeof(Separator), FindResource("EdgeOptionsMenuSeparatorStyle"));

        void Add(string label, Action action)
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        Add("Show profile", showCurrent);
        Add("Refresh names", refresh);
        menu.Items.Add(new Separator { Style = (Style)FindResource("EdgeOptionsMenuSeparatorStyle") });
        autostartItem = new MenuItem { Header = "Launch at startup", IsCheckable = true };
        autostartItem.Click += AutostartChanged;
        menu.Items.Add(autostartItem);
        Add("Diagnostics", showDiagnostics);
        Add("About NSEdge", ShowAbout);
        menu.Items.Add(new Separator { Style = (Style)FindResource("EdgeOptionsMenuSeparatorStyle") });
        Add("Exit NSEdge", exit);
        gearMenu = menu;
        SettingsGearButton.ContextMenu = menu;
    }

    // The smoke test opens the real popup, not an inert menu model.
    public ContextMenu OpenOptionsForSmoke()
    {
        OpenGearClick(this, new RoutedEventArgs());
        return gearMenu ?? throw new InvalidOperationException("Options menu missing.");
    }

    private void OpenGearClick(object sender, RoutedEventArgs e)
    {
        if (gearMenu is null) return;
        if (autostartItem is not null)
        {
            try { autostartItem.IsChecked = StartupManager.IsEnabled(); }
            catch (Exception ex) { Log.Write("Startup entry unavailable: " + ex.GetType().Name); }
        }
        gearMenu.IsOpen = true;
    }

    private void ShowAbout()
    {
        string version = typeof(App).Assembly.GetName().Version?.ToString(2) ?? "unknown";
        System.Windows.MessageBox.Show(this,
            $"NSEdge v{version}\nSony DualSense Edge companion\n\nNS · MIT licensed",
            "About NSEdge", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void AutostartChanged(object sender, RoutedEventArgs e)
    {
        if (initializing || restoringAutostart || autostartItem is null)
            return;
        try { StartupManager.SetEnabled(autostartItem.IsChecked); }
        catch (Exception ex)
        {
            Log.Write("Could not change startup entry: " + ex.GetType().Name);
            restoringAutostart = true;
            try { autostartItem.IsChecked = StartupManager.IsEnabled(); }
            catch { autostartItem.IsChecked = false; }
            finally { restoringAutostart = false; }
            System.Windows.MessageBox.Show(this, "Could not update Windows startup settings.",
                "Windows startup", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BatteryOptionsClick(object sender, RoutedEventArgs e)
    {
        var window = new BatterySettingsWindow(settings, store) { Owner = this };
        window.ShowDialog();
    }

    private void LightingOptionsClick(object sender, RoutedEventArgs e)
    {
        new LightingSettingsWindow(settings, store, () =>
        {
            lightingChanged();
            UpdateLightingStatus();
        }, lightingStatus) { Owner = this }.ShowDialog();
        UpdateLightingStatus();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (System.Windows.Application.Current is App { IsExiting: true })
            return;
        e.Cancel = true;
        Hide();
    }
}