using System.IO;
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
    private NativeLightingService? lighting;
    private Forms.ToolStripMenuItem? disconnectMenuItem;
    private ProfileOverlay? overlay;
    private BatteryOverlay? batteryOverlay;
    private DispatcherTimer? freshnessTimer;
    private EdgeBattery lastBattery = EdgeBattery.Unavailable;
    private byte? lastBtProfileCandidate;
    private readonly LowBatteryPolicy batteryPolicy = new();
    private MainWindow? settingsWindow;
    private AppSettings? settings;
    private SettingsStore store = new();
    private bool diagnosticsRunning;
    private EdgeProfile lastProfile;
    private byte? lastUsbProfileCode;
    private readonly ProfileNotificationTracker profileNotifications = new();
    public bool IsExiting { get; private set; }
    public EdgeBattery CurrentBattery => monitor is { IsReportFresh: true } active
        ? active.Current.Battery : EdgeBattery.Unavailable;

    private void OnStart(object sender, StartupEventArgs e)
    {
        bool smokeTest = e.Args.Contains("--smoke-test", StringComparer.Ordinal);
        if (smokeTest)
        {
            string smokeDirectory = Path.Combine(Path.GetTempPath(), "NSEdge-smoke", Guid.NewGuid().ToString("N"));
            Log.InitializeForSmokeTest(smokeDirectory);
            store = new SettingsStore(Path.Combine(smokeDirectory, "settings.json"));
        }
        mutex = new Mutex(initiallyOwned: true,
            name: smokeTest ? @"Local\NSEdge-Smoke-" + Guid.NewGuid().ToString("N") : @"Local\NSEdge-NS",
            createdNew: out bool first);
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
            if (!smokeTest)
                store.SaveFailed += message => System.Windows.MessageBox.Show(message,
                    "NSEdge settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            overlay = new ProfileOverlay();
            batteryOverlay = new BatteryOverlay();
            settingsWindow = new MainWindow(settings, store, ShowCurrent, () => monitor?.RefreshNames(), () => lighting?.Reset(), () => lighting?.Status ?? "Lighting is off", ShowDiagnostics, () => Shutdown());
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
                        var lightingSettingsTest = new LightingSettingsWindow(settings, store, () => { }, () => "Lighting is off") { Owner = settingsWindow };
                        lightingSettingsTest.Show();
                        lightingSettingsTest.UpdateLayout();
                        lightingSettingsTest.AssertDarkSelector();
                        string? previewDir = Environment.GetEnvironmentVariable("NSEDGE_PREVIEW_PATH");
                        if (!string.IsNullOrWhiteSpace(previewDir))
                            UiPreview.Save(lightingSettingsTest,
                                Path.Combine(Path.GetDirectoryName(Path.GetFullPath(previewDir))!, "lighting-settings.png"));
                        if (lightingSettingsTest.ActualHeight > SystemParameters.WorkArea.Height - 32)
                            throw new InvalidOperationException("Lighting settings exceeds desktop work area.");
                        var pickerTest = new ColorPickerWindow(new LightbarColor(0x75, 0xB8, 0xFF)) { Owner = lightingSettingsTest };
                        pickerTest.Show();
                        pickerTest.UpdateLayout();
                        if (!string.IsNullOrWhiteSpace(previewDir))
                            UiPreview.Save(pickerTest,
                                Path.Combine(Path.GetDirectoryName(Path.GetFullPath(previewDir))!, "colour-picker.png"));
                        if (pickerTest.ActualHeight > SystemParameters.WorkArea.Height - 32)
                            throw new InvalidOperationException("Colour picker exceeds desktop work area.");
                        pickerTest.Close();
                        var confirmationTest = new LightingConfirmationWindow { Owner = lightingSettingsTest };
                        confirmationTest.Show();
                        confirmationTest.UpdateLayout();
                        if (!string.IsNullOrWhiteSpace(previewDir))
                            UiPreview.Save(confirmationTest,
                                Path.Combine(Path.GetDirectoryName(Path.GetFullPath(previewDir))!, "lighting-confirmation.png"));
                        if (confirmationTest.ActualHeight > SystemParameters.WorkArea.Height - 32)
                            throw new InvalidOperationException("Lighting confirmation exceeds desktop work area.");
                        confirmationTest.Close();
                        lightingSettingsTest.Close();

                        var diagnosticsTest = new DiagnosticsWindow(
                            "NSEdge diagnostics\nConnected: True; Transport: Bluetooth\n" +
                            "Input fresh and same connection: True\nProfile: Default\nBattery: 90–99% · Discharging\n" +
                            "DS4Windows compatibility (read-only):\nStatus: Matched\nMatched slot: 1\nTransport: BT\n")
                        { Owner = settingsWindow };
                        diagnosticsTest.Show();
                        diagnosticsTest.UpdateLayout();
                        diagnosticsTest.AssertAppearanceForSmoke();
                        if (!string.IsNullOrWhiteSpace(previewDir))
                            UiPreview.Save(diagnosticsTest,
                                Path.Combine(Path.GetDirectoryName(Path.GetFullPath(previewDir))!, "diagnostics.png"));
                        diagnosticsTest.Close();

                        var popupTest = settingsWindow.OpenOptionsForSmoke();
                        popupTest.UpdateLayout();
                        if (!popupTest.IsOpen || popupTest.Items.Count != 8 ||
                            popupTest.Template.FindName("MenuSurface", popupTest) is null)
                            throw new InvalidOperationException("Themed options popup did not open.");
                        if (!string.IsNullOrWhiteSpace(previewDir))
                            UiPreview.Save(popupTest,
                                Path.Combine(Path.GetDirectoryName(Path.GetFullPath(previewDir))!, "gear-menu.png"), minWidth: 180, minHeight: 130);
                        popupTest.IsOpen = false;
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
            menu.Items.Add("Show profile", null, (_, _) => ShowCurrent());
            menu.Items.Add("Refresh profile names", null, (_, _) => monitor?.RefreshNames());
            menu.Items.Add("Diagnostics", null, (_, _) => ShowDiagnostics());
            disconnectMenuItem = new Forms.ToolStripMenuItem("Disconnect Bluetooth controller", null,
                (_, _) => DisconnectBluetoothController()) { Enabled = false };
            menu.Items.Add(disconnectMenuItem);
            menu.Opening += (_, _) =>
            {
                if (disconnectMenuItem is null) return;
                bool running = false;
                try
                {
                    var processes = System.Diagnostics.Process.GetProcessesByName("DS4Windows");
                    running = processes.Length > 0;
                    foreach (var process in processes) process.Dispose();
                }
                catch (System.ComponentModel.Win32Exception) { }
                disconnectMenuItem.Enabled = running && monitor?.Current is
                    { Connected: true, Transport: EdgeTransport.Bluetooth } &&
                    monitor.CurrentControllerIdentity is not null;
            };
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => Shutdown());
            TrayMenuTheme.Apply(menu);
            tray.ContextMenuStrip = menu;
            tray.MouseDoubleClick += (_, _) => OpenSettings();
            monitor = new EdgeMonitor();
            lighting = new NativeLightingService(monitor, settings);
            monitor.Updated += OnMonitorUpdated;
            monitor.PsPressed += OnPsPressed;
            monitor.Start();
            freshnessTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            freshnessTimer.Tick += (_, _) =>
            {
                if (monitor is { IsReportFresh: false } inactive && inactive.Current.Connected)
                {
                    settingsWindow?.SetStatus(inactive.Current, inputFresh: false);
                    if (tray is not null) tray.Text = "NSEdge: controller input stale";
                }
                lighting?.Update();
                settingsWindow?.UpdateLightingStatus();
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
            settingsWindow?.SetStatus(status, monitor?.IsReportFresh == true);
            lighting?.Update();
            settingsWindow?.UpdateLightingStatus();
            if (disconnectMenuItem is not null)
                disconnectMenuItem.Enabled = status.Connected &&
                    status.Transport == EdgeTransport.Bluetooth &&
                    monitor?.CurrentControllerIdentity is not null;
            if (tray is not null)
            {
                bool reportFresh = monitor?.IsReportFresh == true;
                string label = status.Connected && !reportFresh ? "Controller input stale"
                    : status.Profile == EdgeProfile.Unknown ? "Waiting for controller"
                    : status.Names.DisplayName(status.Profile);
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
            if (status.Connected && status.Transport == EdgeTransport.Bluetooth &&
                status.Detail != lastError)
                Log.Write("Bluetooth status: " + status.Detail);
            lastError = status.Connected && status.Transport == EdgeTransport.Bluetooth
                ? status.Detail
                : status.Connected ? string.Empty : status.Detail;
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
                    Log.Write($"Bluetooth profile code: 0x{rawCandidate:X2}");
            }
            if (status.Transport != EdgeTransport.Bluetooth)
                lastBtProfileCandidate = null;
            if (status.Transport == EdgeTransport.Usb &&
                status.UsbProfileCandidate != lastUsbProfileCode)
            {
                lastUsbProfileCode = status.UsbProfileCandidate;
                if (lastUsbProfileCode is byte code)
                    Log.Write($"USB profile code: 0x{code:X2}");
            }
            if (status.Transport != EdgeTransport.Usb)
                lastUsbProfileCode = null;

            bool announceProfile = profileNotifications.Observe(status.Connected, status.Profile);
            if (status.Profile != lastProfile)
            {
                Log.Write($"Profile: {status.Profile} ({status.Names.DisplayName(status.Profile)}), reports: {status.ReportCount}");
                lastProfile = status.Profile;
            }
            if (announceProfile && settings?.ShowNotifications == true)
                Announce(status);
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
        if (status.Connected && monitor?.IsReportFresh != true)
        {
            overlay?.Announce("Controller input unavailable", settings?.NotificationSeconds ?? 2.5);
            return;
        }
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

    private async void ShowDiagnostics()
    {
        if (diagnosticsRunning || IsExiting) return;
        diagnosticsRunning = true;
        try
        {
        EdgeStatus status = monitor?.Current ?? EdgeStatus.Disconnected;
        string? identity = status.ControllerIdentity;
        // Only stock Query messages are sent, on a worker thread so IPC timeouts
        // cannot freeze the tray or settings window.
        Ds4ReadOnlyProbeResult probe = await Task.Run(() =>
            Ds4WindowsIpc.ProbeReadOnly(identity, status.Transport));
        if (IsExiting || Dispatcher.HasShutdownStarted) return;
        EdgeStatus latest = monitor?.Current ?? EdgeStatus.Disconnected;
        var viewer = new DiagnosticsWindow(DiagnosticsReport.Build(
            status, latest, probe.RedactedSummary,
            settings?.NativeLightingEnabled == true, monitor?.IsReportFresh == true));
        if (settingsWindow is { IsVisible: true })
            viewer.Owner = settingsWindow;
        else
            viewer.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        viewer.ShowDialog();
        }
        catch (Exception ex)
        {
            Log.Write("Diagnostics unavailable: " + ex.GetType().Name);
            if (!IsExiting && !Dispatcher.HasShutdownStarted)
                System.Windows.MessageBox.Show("Diagnostics could not complete. Try again.", "NSEdge",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { diagnosticsRunning = false; }
    }

    private async void DisconnectBluetoothController()
    {
        if (monitor?.Current is not { Connected: true, Transport: EdgeTransport.Bluetooth })
            return;
        EdgeStatus captured = monitor.Current;
        string? identity = captured.ControllerIdentity;
        if (identity is null)
        {
            System.Windows.MessageBox.Show("Controller identity is unavailable.", "NSEdge",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (System.Windows.MessageBox.Show(
            "Ask DS4Windows to disconnect this Bluetooth controller? Gameplay may be interrupted.",
            "Disconnect controller", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        if (!captured.IsSameConnectionAs(monitor.Current) || !monitor.IsReportFresh)
        {
            System.Windows.MessageBox.Show("The controller connection changed. No disconnect was sent.",
                "NSEdge", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (disconnectMenuItem is not null) disconnectMenuItem.Enabled = false;
        try
        {
            string result = await Task.Run(() => Ds4WindowsIpc.DisconnectMatchingBluetoothController(
                identity, () => monitor is { IsReportFresh: true } active &&
                    captured.IsSameConnectionAs(active.Current)));
            Log.Write("DS4Windows disconnect: " + result);
            System.Windows.MessageBox.Show(result, "NSEdge", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log.Write("DS4Windows disconnect unavailable: " + ex.GetType().Name);
            System.Windows.MessageBox.Show("DS4Windows disconnect is unavailable: " + ex.Message,
                "NSEdge", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            if (disconnectMenuItem is not null)
                disconnectMenuItem.Enabled = monitor?.Current is
                    { Connected: true, Transport: EdgeTransport.Bluetooth } &&
                    monitor.CurrentControllerIdentity is not null;
        }
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        IsExiting = true;
        try
        {
            if (lighting is not null)
                lighting.DisposeAsync().AsTask().GetAwaiter().GetResult();
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
