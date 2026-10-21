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
    private EdgeMonitor? monitor;
    private ProfileOverlay? overlay;
    private MainWindow? settingsWindow;
    private AppSettings? settings;
    private readonly SettingsStore store = new();
    private EdgeProfile lastProfile;
    public bool IsExiting { get; private set; }

    private void OnStart(object sender, StartupEventArgs e)
    {
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
            settingsWindow = new MainWindow(settings, store, ShowCurrent, () => monitor?.RefreshNames());
            tray = new Forms.NotifyIcon
            {
                Icon = SystemIcons.Information,
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
            monitor.Start();
            Log.Write("NSEdge started");
        }
        catch (Exception ex)
        {
            Log.Write("Startup failed: " + ex);
            MessageBox.Show(ex.Message, "NSEdge startup error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            if (status.Profile != lastProfile)
            {
                lastProfile = status.Profile;
                if (status.Profile != EdgeProfile.Unknown && settings?.ShowNotifications == true)
                    Announce(status);
            }
        }, DispatcherPriority.Background);
    }

    private void Announce(EdgeStatus status) => overlay?.Announce(
        $"{status.Names.DisplayName(status.Profile)}  ({EdgeProfileDecoder.Shortcut(status.Profile)})",
        settings?.NotificationSeconds ?? 2.5);

    private void ShowCurrent()
    {
        EdgeStatus status = monitor?.Current ?? EdgeStatus.Disconnected;
        if (status.Profile == EdgeProfile.Unknown)
            overlay?.Announce("Controller not detected", settings?.NotificationSeconds ?? 2.5);
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
        MessageBox.Show(
            $"Connected: {status.Connected}\nProfile: {status.Profile}\n" +
            $"Name: {status.Names.DisplayName(status.Profile)}\n" +
            $"Circle: {status.Names.Circle ?? "(not read)"}\n" +
            $"Cross: {status.Names.Cross ?? "(not read)"}\n" +
            $"Square: {status.Names.Square ?? "(not read)"}\n" +
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
                monitor.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        catch (Exception ex) { Log.Write("Shutdown error: " + ex); }
        overlay?.Stop();
        if (tray is not null)
        {
            tray.Visible = false;
            tray.Dispose();
        }
        mutex?.ReleaseMutex();
        mutex?.Dispose();
        Log.Write("NSEdge stopped");
    }
}
