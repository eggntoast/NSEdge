using Microsoft.Win32.SafeHandles;
using NSEdge.Core;

namespace NSEdge.Windows.Hid;

public sealed record EdgeStatus(bool Connected, EdgeProfile Profile, ProfileNames Names, long ReportCount, string Detail)
{
    public static readonly EdgeStatus Disconnected = new(false, EdgeProfile.Unknown, ProfileNames.Empty, 0, "Waiting for controller");
}

public sealed class EdgeMonitor : IAsyncDisposable
{
    private readonly CancellationTokenSource shutdown = new();
    private Task? worker;
    private int refreshRequested;
    private EdgeStatus current = EdgeStatus.Disconnected;

    public event Action<EdgeStatus>? Updated;
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
        byte[] buffer = new byte[64];
        long reports = 0;
        Publish(new EdgeStatus(true, EdgeProfile.Unknown, names, reports, "Connected"));

        try
        {
            while (!token.IsCancellationRequested)
            {
                if (Interlocked.Exchange(ref refreshRequested, 0) == 1)
                {
                    names = ReadNames(handle);
                    Publish(new EdgeStatus(true, debounce.Current, names, reports, "Names refreshed"));
                }

                int read = await stream.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
                if (read == 0)
                    break;
                if (read < EdgeProfileDecoder.MinimumReportLength || buffer[0] != 0x01)
                    continue;

                reports++;
                var profile = EdgeProfileDecoder.Decode(buffer.AsSpan(0, read));
                if (debounce.Observe(profile))
                    Publish(new EdgeStatus(true, profile, names, reports, "Input active"));
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
