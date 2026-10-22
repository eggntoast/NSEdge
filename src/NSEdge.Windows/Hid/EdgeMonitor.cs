using Microsoft.Win32.SafeHandles;
using NSEdge.Core;

namespace NSEdge.Windows.Hid;

public sealed record EdgeStatus(bool Connected, EdgeProfile Profile, ProfileNames Names, long ReportCount, string Detail, EdgeBattery Battery)
{
    public byte? BatteryStatusRaw { get; init; }
    public byte? BluetoothProfileCandidate { get; init; }
    public EdgeTransport? Transport { get; init; }
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
                    try { await ReadDeviceAsync(path, token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
                    {
                        Publish(EdgeStatus.Disconnected with { Detail = "Controller unavailable: " + ex.Message });
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
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
        int reportLength = NativeMethods.GetInputReportLength(handle);
        EdgeTransport? transport = EdgeTransportDetector.FromInputReportLength(reportLength);
        if (transport is null)
        {
            Publish(EdgeStatus.Disconnected with { Detail = $"Unsupported HID input report length: {reportLength}" });
            return;
        }

        using var stream = new FileStream(handle, FileAccess.Read, reportLength, isAsync: true);
        using var interrupt = token.Register(static state => ((FileStream)state!).Dispose(), stream);
        Interlocked.Exchange(ref lastReportTicks, 0);

        bool bluetooth = transport == EdgeTransport.Bluetooth;
        ProfileNames names = bluetooth ? ProfileNames.Empty : ReadNames(handle);
        var debounce = new ProfileDebouncer();
        var psButton = new PsPressDetector();
        EdgeBattery battery = EdgeBattery.Unavailable;
        byte? batteryStatusRaw = null;
        byte? profileCandidate = null;
        byte[] buffer = new byte[reportLength];
        long reports = 0;
        bool awaitingExtended = bluetooth;
        string detail = bluetooth ? "Bluetooth connected; waiting for extended input reports"
                                  : "USB connected";

        EdgeStatus Snapshot(string reason) => new(true, bluetooth ? EdgeProfile.Unknown : debounce.Current,
            names, reports, reason, battery)
        {
            Transport = transport,
            BatteryStatusRaw = batteryStatusRaw,
            BluetoothProfileCandidate = profileCandidate
        };

        Publish(Snapshot(detail));

        try
        {
            while (!token.IsCancellationRequested)
            {
                if (Interlocked.Exchange(ref refreshRequested, 0) == 1)
                {
                    if (!bluetooth)
                        names = ReadNames(handle);
                    Publish(Snapshot(bluetooth ? "Bluetooth Sony profile-name access not verified" : "Names refreshed"));
                }

                int read = await stream.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
                if (read == 0)
                    break;

                bool psDown = false;
                bool batteryChanged = false;
                bool profileChanged = false;

                if (bluetooth)
                {
                    var kind = BluetoothInputDecoder.Decode(buffer.AsSpan(0, read), out BluetoothInput input);
                    if (kind == BluetoothReportKind.Minimal)
                    {
                        if (awaitingExtended && detail != "Bluetooth minimal input only; extended reports not enabled")
                        {
                            detail = "Bluetooth minimal input only; extended reports not enabled";
                            Publish(Snapshot(detail));
                        }
                        continue;
                    }
                    if (kind != BluetoothReportKind.Extended)
                        continue;

                    awaitingExtended = false;
                    detail = "Bluetooth input active; onboard profile mapping unverified";
                    psDown = input.PsPressed;
                    batteryChanged = battery != input.Battery;
                    battery = input.Battery;
                    batteryStatusRaw = input.RawBatteryStatus;
                    bool candidateChanged = profileCandidate != input.EdgeProfileCandidate;
                    profileCandidate = input.EdgeProfileCandidate;
                    if (candidateChanged && reports > 0 && reports % 128 == 0)
                        Publish(Snapshot(detail));
                }
                else
                {
                    if (read != 64 || buffer[0] != 0x01)
                        continue;
                    if (EdgeInputDecoder.TryDecode(buffer.AsSpan(0, read), out EdgeBattery nextBattery, out bool pressed))
                    {
                        psDown = pressed;
                        batteryChanged = battery != nextBattery;
                        battery = nextBattery;
                        batteryStatusRaw = buffer[53];
                    }
                    profileChanged = debounce.Observe(EdgeProfileDecoder.Decode(buffer.AsSpan(0, read)));
                    detail = "USB input active";
                }

                reports++;
                Interlocked.Exchange(ref lastReportTicks, DateTime.UtcNow.Ticks);
                if (psButton.Observe(psDown, DateTimeOffset.UtcNow))
                    PsPressed?.Invoke(battery);

                if (profileChanged || batteryChanged || reports == 1)
                    Publish(Snapshot(detail));
            }
        }
        catch (Exception ex) when (token.IsCancellationRequested &&
            ex is IOException or ObjectDisposedException or OperationCanceledException) { }
        finally { Publish(EdgeStatus.Disconnected); }
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
