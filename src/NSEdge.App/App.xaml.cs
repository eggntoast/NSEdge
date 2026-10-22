using System.Drawing;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using NSEdge.App.Overlays;
using NSEdge.App.Services;
using NSEdge.Core;
using NSEdge.Windows.Hid;

namespace NSEdge.App;

public partial class App : System.Windows.Application
{
    private Mutex? mutex;
    private Forms.NotifyIcon? tray;
    private Icon? applicationIcon;
    private bool lastConnected;
    private string lastError = string.Empty;
    private EdgeMonitor? monitor;
    private ProfileOverlay? overlay;
    private BatteryOverlay? batteryOverlay;
    private DispatcherTimer? freshnessTimer;
    private EdgeBattery lastBattery = EdgeBattery.Unavailable;
    private byte? lastBtProfileCandidate;
    private readonly LowBatteryPolicy batteryPolicy = new();
    private MainWindow? settingsWindow;
    private AppSettings? settings;
    private readonly SettingsStore store = new();
    private EdgeProfile lastProfile;
    public bool IsExiting { get; private set; }

    private void OnStart(object sender, StartupEventArgs e)
    {
        bool smokeTest = e.Args.Contains("--smoke-test", StringComparer.Ordinal);
        mutex = new Mutex(initiallyOwned: true, name: @"Local\NSEdge-NS", createdNew: out bool first);
        if (!first)
        {
            mutex.Dispose();
            mutex = null;
            Shutdown();
            return;
        }

        try
        {
            settings = store.Load();
            overlay = new ProfileOverlay();
            batteryOverlay = new BatteryOverlay();
            settingsWindow = new MainWindow(settings, store, ShowCurrent, () => monitor?.RefreshNames());
            if (smokeTest)
            {
                using Icon? testIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
                if (testIcon is null)
                    throw new InvalidOperationException("The executable icon could not be extracted.");
                settingsWindow.Show();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        settingsWindow.AssertNormalLayout();
                        var batterySettingsTest = new BatterySettingsWindow(settings, store) { Owner = settingsWindow };
                        batterySettingsTest.Show();
                        batterySettingsTest.UpdateLayout();
                        if (batterySettingsTest.ActualHeight > SystemParameters.WorkArea.Height - 32)
                            throw new InvalidOperationException("Battery settings exceeds desktop work area.");
                        batterySettingsTest.Close();
                        string? preview = Environment.GetEnvironmentVariable("NSEDGE_PREVIEW_PATH");
                        if (!string.IsNullOrWhiteSpace(preview))
                        {
                            settingsWindow.SavePreview(preview);
                            Log.Write("WPF preview saved: " + preview);
                        }
                        Log.Write("WPF startup and layout smoke test passed");
                        Shutdown();
                    }
                    catch (Exception ex)
                    {
                        Log.Write("WPF layout smoke test failed: " + ex);
                        Shutdown(1);
                    }
                }), DispatcherPriority.ApplicationIdle);
                return;
            }
            applicationIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!)
                ?? throw new InvalidOperationException("The embedded NSEdge icon could not be loaded.");
            tray = new Forms.NotifyIcon
            {
                Icon = applicationIcon,
                Visible = true,
                Text = "NSEdge: waiting for controller"
            };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Open NSEdge", null, (_, _) => OpenSettings());
            menu.Items.Add("Show current profile", null, (_, _) => ShowCurrent());
            menu.Items.Add("Refresh names", null, (_, _) => monitor?.RefreshNames());
            menu.Items.Add("Diagnostics", null, (_, _) => ShowDiagnostics());
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => Shutdown());
            tray.ContextMenuStrip = menu;
            tray.MouseDoubleClick += (_, _) => OpenSettings();
            monitor = new EdgeMonitor();
            monitor.Updated += OnMonitorUpdated;
            monitor.PsPressed += OnPsPressed;
            monitor.Start();
            freshnessTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            freshnessTimer.Tick += (_, _) =>
            {
                if (settingsWindow is not null && monitor is { IsReportFresh: false } && monitor.Current.Connected)
                    settingsWindow.SetStatus(monitor.Current with { Battery = EdgeBattery.Unavailable });
            };
            freshnessTimer.Start();
            Log.Write("NSEdge started: " + typeof(App).Assembly.GetName().Version);
        }
        catch (Exception ex)
        {
            Log.Write("Startup failed: " + ex);
            if (!smokeTest)
                System.Windows.MessageBox.Show(ex.Message, "NSEdge startup error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnMonitorUpdated(EdgeStatus status)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;
        Dispatcher.BeginInvoke(() =>
        {
            if (IsExiting)
                return;
            settingsWindow?.SetStatus(status);
            if (tray is not null)
            {
                string label = status.Profile == EdgeProfile.Unknown ? "Waiting for controller" : status.Names.DisplayName(status.Profile);
                string caption = "NSEdge: " + label;
                tray.Text = caption[..Math.Min(caption.Length, 63)];
            }
            if (status.Connected != lastConnected)
            {
                lastConnected = status.Connected;
                Log.Write(status.Connected
                    ? $"DualSense Edge connected over {status.Transport}"
                    : "DualSense Edge disconnected");
            }
            if (!status.Connected && status.Detail != lastError &&
                !string.Equals(status.Detail, "Waiting for controller", StringComparison.Ordinal))
                Log.Write("Controller status: " + status.Detail);
            lastError = status.Connected ? string.Empty : status.Detail;
            if (!status.Connected)
            {
                batteryPolicy.Reset();
                lastBattery = EdgeBattery.Unavailable;
            }
            else if (status.Battery != lastBattery)
            {
                lastBattery = status.Battery;
                Log.Write("Battery: " + status.Battery.Description);
            }

            if (status.Connected && settings?.ShowLowBatteryAlerts == true &&
                batteryPolicy.Observe(status.Battery, settings.LowBatteryThresholdPercent))
            {
                Log.Write("Low battery threshold reached: " + status.Battery.LevelText);
                batteryOverlay?.Announce(status.Battery, settings.NotificationSeconds, lowWarning: true);
            }
            if (status.Transport == EdgeTransport.Bluetooth &&
                status.BluetoothProfileCandidate != lastBtProfileCandidate)
            {
                lastBtProfileCandidate = status.BluetoothProfileCandidate;
                if (lastBtProfileCandidate is byte rawCandidate)
                    Log.Write($"Bluetooth Edge profile candidate (unverified): 0x{rawCandidate:X2}");
            }
            if (status.Transport != EdgeTransport.Bluetooth)
                lastBtProfileCandidate = null;

            if (status.Profile != lastProfile)
            {
                Log.Write($"Profile: {status.Profile} ({status.Names.DisplayName(status.Profile)}), reports: {status.ReportCount}");
                lastProfile = status.Profile;
                if (status.Profile != EdgeProfile.Unknown && settings?.ShowNotifications == true)
                    Announce(status);
            }
        }, DispatcherPriority.Background);
    }

    private void OnPsPressed(EdgeBattery battery)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsExiting && settings?.ShowBatteryOnPs == true && monitor?.IsReportFresh == true)
                batteryOverlay?.Announce(battery, settings.NotificationSeconds);
        }), DispatcherPriority.Background);
    }

    private void Announce(EdgeStatus status) => overlay?.Announce(
        $"{status.Names.DisplayName(status.Profile)}  ({EdgeProfileDecoder.Shortcut(status.Profile)})",
        settings?.NotificationSeconds ?? 2.5);

    private void ShowCurrent()
    {
        EdgeStatus status = monitor?.Current ?? EdgeStatus.Disconnected;
        if (status.Profile == EdgeProfile.Unknown)
            overlay?.Announce(status.Connected && status.Transport == EdgeTransport.Bluetooth
                ? "Bluetooth profile not yet verified"
                : "Controller not detected", settings?.NotificationSeconds ?? 2.5);
        else
            Announce(status);
    }

    private void OpenSettings()
    {
        if (settingsWindow is null)
            return;
        settingsWindow.Show();
        settingsWindow.Activate();
    }

    private void ShowDiagnostics()
    {
        EdgeStatus status = monitor?.Current ?? EdgeStatus.Disconnected;
        System.Windows.MessageBox.Show(
            $"Connected: {status.Connected}\nTransport: {status.Transport?.ToString() ?? "None"}\nProfile: {status.Profile}\n" +
            $"Name: {status.Names.DisplayName(status.Profile)}\n" +
            $"Circle: {status.Names.Circle ?? "(not read)"}\n" +
            $"Cross: {status.Names.Cross ?? "(not read)"}\n" +
            $"Square: {status.Names.Square ?? "(not read)"}\n" +
            $"Battery: {status.Battery.Description}\n" +
            $"Battery status byte {(status.Transport == EdgeTransport.Bluetooth ? 54 : 53)}: {(status.BatteryStatusRaw is byte raw ? $"0x{raw:X2}" : "Unavailable")}\n" +
            $"BT Edge candidate byte 50 (unverified): {(status.BluetoothProfileCandidate is byte candidate ? $"0x{candidate:X2}" : "Unavailable")}\n" +
            $"Reports: {status.ReportCount}\nStatus: {status.Detail}\nLog: {Log.Path}",
            "NSEdge diagnostics", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        IsExiting = true;
        try
        {
            if (monitor is not null)
            {
                monitor.Updated -= OnMonitorUpdated;
                monitor.PsPressed -= OnPsPressed;
                monitor.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        catch (Exception ex) { Log.Write("Shutdown error: " + ex); }
        freshnessTimer?.Stop();
        batteryOverlay?.Stop();
        overlay?.Stop();
        if (tray is not null)
        {
            tray.Visible = false;
            tray.Dispose();
        }
        applicationIcon?.Dispose();
        mutex?.ReleaseMutex();
        mutex?.Dispose();
        Log.Write("NSEdge stopped");
    }
}
