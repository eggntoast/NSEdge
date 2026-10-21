using System.ComponentModel;
using System.Windows;
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
        initializing = false;
    }

    public void SetStatus(EdgeStatus status)
    {
        ConnectionText.Text = status.Connected ? "DualSense Edge connected (USB)" : status.Detail;
        ProfileText.Text = status.Profile == NSEdge.Core.EdgeProfile.Unknown
            ? "No active profile" : $"{status.Names.DisplayName(status.Profile)}  ·  {NSEdge.Core.EdgeProfileDecoder.Shortcut(status.Profile)}";
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
        if (initializing)
            return;
        try { StartupManager.SetEnabled(AutostartCheck.IsChecked == true); }
        catch (Exception ex)
        {
            Log.Write("Could not change startup entry: " + ex);
            MessageBox.Show(this, ex.Message, "Windows startup", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowCurrentClick(object sender, RoutedEventArgs e) => showCurrent();
    private void RefreshClick(object sender, RoutedEventArgs e) => refresh();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (Application.Current is App { IsExiting: true })
            return;
        e.Cancel = true;
        Hide();
    }
}
