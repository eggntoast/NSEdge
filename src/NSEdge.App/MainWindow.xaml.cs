using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
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
        ConnectionBadgeText.Text = connected ? "Connected" : status.Detail.StartsWith("Controller unavailable", StringComparison.Ordinal) ? "Unavailable" : "Searching";
        StatusDot.Fill = (System.Windows.Media.Brush)FindResource(connected ? "OnlineBrush" : "OfflineBrush");
        ConnectionBadgeText.Foreground = (System.Windows.Media.Brush)FindResource(connected ? "OnlineBrush" : "SecondaryTextBrush");
        ConnectionText.Text = connected ? "DualSense Edge  ·  USB" : status.Detail;
        ProfileText.Text = status.Profile == EdgeProfile.Unknown
            ? connected ? "Waiting for input" : "No active profile"
            : status.Names.DisplayName(status.Profile);
        ShortcutText.Text = status.Profile == EdgeProfile.Unknown ? "—" : EdgeProfileDecoder.Shortcut(status.Profile);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Rect available = SystemParameters.WorkArea;
        Width = Math.Min(Width, Math.Max(340, available.Width - 24));
        Height = Math.Min(Height, Math.Max(320, available.Height - 24));
        Left = available.Left + (available.Width - Width) / 2;
        Top = available.Top + (available.Height - Height) / 2;
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
