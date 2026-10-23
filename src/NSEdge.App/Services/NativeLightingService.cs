using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using NSEdge.Core;
using NSEdge.Windows.Hid;

namespace NSEdge.App.Services;

internal sealed class NativeLightingService(EdgeMonitor monitor, AppSettings settings) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource stopping = new();
    private Task? active;
    private string? lastWrite;
    private string lastStatus = "Lighting is off";
    private byte sequence;

    public string Status => Volatile.Read(ref lastStatus);

    public void Update()
    {
        if (active is { IsCompleted: false }) return;
        active = Task.Run(EvaluateAsync);
    }

    public void Reset()
    {
        lastWrite = null;
        Update();
    }

    private async Task EvaluateAsync()
    {
        try
        {
            await gate.WaitAsync(stopping.Token).ConfigureAwait(false);
            try
            {
                if (!settings.NativeLightingEnabled)
                {
                    lastWrite = null;
                    SetStatus("Lighting is off");
                    return;
                }

                EdgeStatus state = monitor.Current;
                string? path = monitor.CurrentDevicePath;
                if (!state.Connected || !monitor.IsReportFresh || path is null || state.Transport is null)
                {
                    lastWrite = null;
                    SetStatus("Waiting for a controller");
                    return;
                }

                LightbarPalette? palette = settings.LightingPalette();
                if (palette is null || !Enum.IsDefined(settings.LightingMode))
                {
                    SetStatus("Invalid lighting settings");
                    return;
                }

                LightbarColor? color = LightbarPolicy.Select(state.Battery, settings.LightingMode, palette);
                if (color is null)
                {
                    SetStatus("Battery status unavailable; lighting unchanged");
                    return;
                }

                if (!CanOwnOutput())
                {
                    lastWrite = null;
                    SetStatus("Other controller software detected; native lighting paused");
                    return;
                }

                string key = $"{path}|{color.Value.Hex}";
                if (lastWrite == key) return;
                lastWrite = key;

                if (stopping.IsCancellationRequested || !settings.NativeLightingEnabled ||
                    !string.Equals(monitor.CurrentDevicePath, path, StringComparison.Ordinal) ||
                    !CanOwnOutput())
                {
                    lastWrite = null;
                    return;
                }

                try
                {
                    // The report only enables RGB lightbar fields; all other valid flags remain unset.
                    NativeLightbar.WriteColor(path, state.Transport.Value, color.Value, sequence++);
                    SetStatus("Lightbar command sent; physical response not verified");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                    ArgumentException or Win32Exception or NotSupportedException)
                {
                    SetStatus("Lighting unavailable: " + ex.GetType().Name);
                }
            }
            finally { gate.Release(); }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
    }

    private static bool CanOwnOutput()
    {
        try
        {
            // Process detection is a fail-closed compatibility guard, not an interprocess lock.
            foreach (string name in new[] { "DS4Windows", "DualSenseX", "DSX", "reWASD" })
            {
                using var procList = ProcessList.Get(name);
                if (procList.Present) return false;
            }
            return true;
        }
        catch (Win32Exception) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private void SetStatus(string value)
    {
        if (Status == value) return;
        Volatile.Write(ref lastStatus, value);
        Log.Write("Native lighting: " + value);
    }

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync().ConfigureAwait(false);
        if (active is not null) await active.ConfigureAwait(false);
        stopping.Dispose();
        gate.Dispose();
    }

    private sealed class ProcessList : IDisposable
    {
        private readonly Process[] processes;
        private ProcessList(Process[] processes) => this.processes = processes;
        public bool Present => processes.Length > 0;
        public static ProcessList Get(string name) => new(Process.GetProcessesByName(name));
        public void Dispose()
        {
            foreach (Process process in processes) process.Dispose();
        }
    }
}
