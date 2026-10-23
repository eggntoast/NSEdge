using System.ComponentModel;
using System.IO;
using System.Windows;
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
    private bool initializing = true;
    private bool restoringAutostart;

    public MainWindow(AppSettings settings, SettingsStore store, Action showCurrent, Action refresh)
    {
        InitializeComponent();
        this.settings = settings;
        this.store = store;
        this.showCurrent = showCurrent;
        this.refresh = refresh;
        NotificationsCheck.IsChecked = settings.ShowNotifications;
        DurationSlider.Value = settings.NotificationSeconds;
        DurationText.Text = $"{settings.NotificationSeconds:0.0} s";
        try { AutostartCheck.IsChecked = StartupManager.IsEnabled(); }
        catch (Exception ex) { Log.Write("Startup entry unavailable: " + ex.Message); }
        VersionText.Text = $"v{typeof(App).Assembly.GetName().Version?.ToString(3)}  ·  Developed by NS";
        initializing = false;
    }

    public void SetStatus(EdgeStatus status)
    {
        bool connected = status.Connected;
        bool unavailable = !connected &&
            (status.Detail.Contains("unavailable", StringComparison.OrdinalIgnoreCase) ||
             status.Detail.Contains("unsupported", StringComparison.OrdinalIgnoreCase) ||
             status.Detail.Contains("not found", StringComparison.OrdinalIgnoreCase));
        ConnectionBadgeText.Text = connected ? "Connected" : unavailable ? "Unavailable" : "Searching";
        StatusDot.Fill = (System.Windows.Media.Brush)FindResource(connected ? "OnlineBrush" : "OfflineBrush");
        ConnectionBadgeText.Foreground = (System.Windows.Media.Brush)FindResource(connected ? "OnlineBrush" : "SecondaryTextBrush");
        ConnectionText.Text = connected
            ? status.Transport == EdgeTransport.Bluetooth ? "DualSense Edge  ·  Bluetooth" : "DualSense Edge  ·  USB"
            : status.Detail;
        ProfileText.ToolTip = status.NamesFromUsbCache
            ? "Name saved from an earlier USB connection. Reconnect by USB after changing Sony profiles."
            : null;
        ProfileText.Text = status.Profile == EdgeProfile.Unknown
            ? connected && status.Transport == EdgeTransport.Bluetooth
                ? "Profile not verified"
                : connected ? "Waiting for input" : "No active profile"
            : status.Names.DisplayName(status.Profile);
        ShortcutText.Text = status.Profile == EdgeProfile.Unknown ? "—" : EdgeProfileDecoder.Shortcut(status.Profile);
        BatteryText.Text = status.Connected
            ? "Battery: " + status.Battery.Description
            : "Battery unavailable";
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var area = SystemParameters.WorkArea;
        MaxHeight = Math.Max(260, area.Height - 32);
        MaxWidth = Math.Max(320, area.Width - 32);
        Width = Math.Min(514, MaxWidth);

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
        if (ContentViewport.ComputedVerticalScrollBarVisibility == Visibility.Visible ||
            ContentViewport.ScrollableHeight > 0.75)
            throw new InvalidOperationException($"Unexpected settings scrollbar: {ContentViewport.ScrollableHeight:0.##} DIP.");
        if (FooterPanel.ActualHeight < 30 || ContentLayout.ActualWidth < 300)
            throw new InvalidOperationException("Settings content has not been measured correctly.");

        System.Windows.Point footerEnd = FooterPanel.TranslatePoint(new System.Windows.Point(0, FooterPanel.ActualHeight), ContentViewport);
        if (footerEnd.Y > ContentViewport.ActualHeight + 0.75)
            throw new InvalidOperationException("Settings footer is clipped.");
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

    private void AutostartChanged(object sender, RoutedEventArgs e)
    {
        if (initializing || restoringAutostart)
            return;
        try { StartupManager.SetEnabled(AutostartCheck.IsChecked == true); }
        catch (Exception ex)
        {
            Log.Write("Could not change startup entry: " + ex);
            restoringAutostart = true;
            try { AutostartCheck.IsChecked = StartupManager.IsEnabled(); }
            catch { AutostartCheck.IsChecked = false; }
            finally { restoringAutostart = false; }
            System.Windows.MessageBox.Show(this, ex.Message, "Windows startup", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BatteryOptionsClick(object sender, RoutedEventArgs e)
    {
        var window = new BatterySettingsWindow(settings, store) { Owner = this };
        window.ShowDialog();
    }

    private void ShowCurrentClick(object sender, RoutedEventArgs e) => showCurrent();
    private void RefreshClick(object sender, RoutedEventArgs e) => refresh();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (System.Windows.Application.Current is App { IsExiting: true })
            return;
        e.Cancel = true;
        Hide();
    }
}