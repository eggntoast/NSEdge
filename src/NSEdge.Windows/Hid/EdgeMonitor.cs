using Microsoft.Win32.SafeHandles;
using NSEdge.Core;

namespace NSEdge.Windows.Hid;

public sealed record EdgeStatus(bool Connected, EdgeProfile Profile, ProfileNames Names, long ReportCount, string Detail, EdgeBattery Battery)
{
    public static readonly EdgeStatus Disconnected = new(false, EdgeProfile.Unknown, ProfileNames.Empty, 0, "Waiting for controller", EdgeBattery.Unavailable);
}

public sealed class EdgeMonitor : IAsyncDisposable
{
    private readonly CancellationTokenSource shutdown = new();
    private Task? worker;
    private int refreshRequested;
    private EdgeStatus current = EdgeStatus.Disconnected;
    private long lastReportTicks;

    public event Action<EdgeStatus>? Updated;
    public event Action<EdgeBattery>? PsPressed;
    public bool IsReportFresh => DateTime.UtcNow.Ticks - Interlocked.Read(ref lastReportTicks) < TimeSpan.FromSeconds(15).Ticks;
    public EdgeStatus Current => Volatile.Read(ref current);

    public void Start()
    {
        if (worker is not null)
            throw new InvalidOperationException("Monitor is already running.");
        worker = Task.Run(() => RunAsync(shutdown.Token));
    }

    public void RefreshNames() => Interlocked.Exchange(ref refreshRequested, 1);

    private void Publish(EdgeStatus state)
    {
        Volatile.Write(ref current, state);
        Updated?.Invoke(state);
    }

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                foreach (string path in NativeMethods.FindEdgePaths())
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        await ReadDeviceAsync(path, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
                    {
                        Publish(EdgeStatus.Disconnected with { Detail = "Controller unavailable: " + ex.Message });
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Publish(EdgeStatus.Disconnected with { Detail = "Device discovery failed: " + ex.Message });
            }

            try { await Task.Delay(TimeSpan.FromSeconds(2), token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
        Publish(EdgeStatus.Disconnected);
    }

    private async Task ReadDeviceAsync(string path, CancellationToken token)
    {
        using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite, FileOptions.Asynchronous);
        using var stream = new FileStream(handle, FileAccess.Read, 64, isAsync: true);
        using var interrupt = token.Register(static state => ((FileStream)state!).Dispose(), stream);

        ProfileNames names = ReadNames(handle);
        var debounce = new ProfileDebouncer();
        var psButton = new PsPressDetector();
        EdgeBattery battery = EdgeBattery.Unavailable;
        byte[] buffer = new byte[64];
        long reports = 0;
        Publish(new EdgeStatus(true, EdgeProfile.Unknown, names, reports, "Connected", battery));

        try
        {
            while (!token.IsCancellationRequested)
            {
                if (Interlocked.Exchange(ref refreshRequested, 0) == 1)
                {
                    names = ReadNames(handle);
                    Publish(new EdgeStatus(true, debounce.Current, names, reports, "Names refreshed", battery));
                }

                int read = await stream.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
                if (read == 0)
                    break;
                if (read < EdgeProfileDecoder.MinimumReportLength || buffer[0] != 0x01)
                    continue;

                reports++;
                Interlocked.Exchange(ref lastReportTicks, DateTime.UtcNow.Ticks);
                bool batteryChanged = false;
                if (EdgeInputDecoder.TryDecode(buffer.AsSpan(0, read), out EdgeBattery nextBattery, out bool psDown))
                {
                    batteryChanged = battery != nextBattery;
                    battery = nextBattery;
                    if (psButton.Observe(psDown, DateTimeOffset.UtcNow))
                        PsPressed?.Invoke(battery);
                }

                var profile = EdgeProfileDecoder.Decode(buffer.AsSpan(0, read));
                bool profileChanged = debounce.Observe(profile);
                if (profileChanged || batteryChanged)
                    Publish(new EdgeStatus(true, debounce.Current, names, reports, "Input active", battery));
            }
        }
        catch (Exception ex) when (token.IsCancellationRequested && ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
        }
        finally
        {
            Publish(EdgeStatus.Disconnected);
        }
    }

    private static ProfileNames ReadNames(SafeFileHandle handle)
    {
        static string? ReadSlot(SafeFileHandle handle, byte reportId)
        {
            byte[]? first = NativeMethods.GetFeature(handle, reportId);
            byte[]? second = NativeMethods.GetFeature(handle, (byte)(reportId + 1));
            return first is null || second is null ? null : ProfileNameDecoder.Decode(first, second);
        }
        return new ProfileNames(ReadSlot(handle, 0x79), ReadSlot(handle, 0x76), ReadSlot(handle, 0x73));
    }

    public async ValueTask DisposeAsync()
    {
        await shutdown.CancelAsync().ConfigureAwait(false);
        if (worker is not null)
        {
            try { await worker.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        shutdown.Dispose();
    }
}
