using Microsoft.Win32.SafeHandles;
using NSEdge.Core;

namespace NSEdge.Windows.Hid;

public sealed record EdgeStatus(bool Connected, EdgeProfile Profile, ProfileNames Names, long ReportCount, string Detail, EdgeBattery Battery)
{
    public byte? BatteryStatusRaw { get; init; }
    public byte? BluetoothProfileCandidate { get; init; }
    public EdgeTransport? Transport { get; init; }
    public string ScanSummary { get; init; } = "Not scanned";
    public long MinimalReports { get; init; }
    public long BadCrcReports { get; init; }
    public DateTimeOffset? LastInputUtc { get; init; }
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
                HidScanResult scan = NativeMethods.ScanEdgeDevices();
                if (scan.Devices.Count == 0)
                {
                    string reason = scan.EdgeInterfaces == 0
                        ? "DualSense Edge HID interface not found"
                        : "DualSense Edge interface found, but input format is unsupported";
                    if (Current.Detail != reason || Current.ScanSummary != scan.Summary)
                        Publish(EdgeStatus.Disconnected with { Detail = reason, ScanSummary = scan.Summary });
                }

                foreach (HidDevice device in scan.Devices)
                {
                    token.ThrowIfCancellationRequested();
                    try { await ReadDeviceAsync(device, scan.Summary, token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
                    {
                        Publish(EdgeStatus.Disconnected with {
                            Detail = "Edge HID input unavailable: " + ex.Message,
                            ScanSummary = scan.Summary });
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

    private async Task ReadDeviceAsync(HidDevice device, string scanSummary, CancellationToken token)
    {
        using SafeFileHandle handle = File.OpenHandle(device.Path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite, FileOptions.Asynchronous);
        int reportLength = NativeMethods.GetInputReportLength(handle);
        EdgeTransport? transport = EdgeTransportDetector.FromInputReportLength(reportLength);
        if (transport is null || transport != device.Transport)
        {
            Publish(EdgeStatus.Disconnected with { Detail = $"Unsupported HID input report length: {reportLength}", ScanSummary = scanSummary });
            return;
        }

        using var stream = new FileStream(handle, FileAccess.Read, reportLength, isAsync: true);
        using var interrupt = token.Register(static state => ((FileStream)state!).Dispose(), stream);
        Interlocked.Exchange(ref lastReportTicks, 0);

        bool bluetooth = transport == EdgeTransport.Bluetooth;
        ProfileNames names = bluetooth ? ProfileNames.Empty : ReadNames(handle);
        var debounce = new ProfileDebouncer();
        var psButton = new PsPressDetector();
        var btCandidateObserver = new StableByteObserver();
        EdgeBattery battery = EdgeBattery.Unavailable;
        byte? batteryStatusRaw = null;
        byte? profileCandidate = null;
        bool btCandidateChanged = false;
        byte[] buffer = new byte[reportLength];
        long reports = 0, minimalReports = 0, badCrcReports = 0;
        DateTimeOffset? lastInput = null;
        bool awaitingExtended = bluetooth;
        string detail = bluetooth ? "Bluetooth connected; waiting for extended input reports"
                                  : "USB connected";

        EdgeStatus Snapshot(string reason) => new(true, debounce.Current,
            names, reports, reason, battery)
        {
            Transport = transport,
            BatteryStatusRaw = batteryStatusRaw,
            BluetoothProfileCandidate = profileCandidate,
            ScanSummary = scanSummary,
            MinimalReports = minimalReports,
            BadCrcReports = badCrcReports,
            LastInputUtc = lastInput
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
                        minimalReports++;
                        if (awaitingExtended && detail != "Bluetooth minimal input only; extended reports not enabled")
                        {
                            detail = "Bluetooth minimal input only; extended reports not enabled";
                            Publish(Snapshot(detail));
                        }
                        continue;
                    }
                    if (kind != BluetoothReportKind.Extended)
                    {
                        if (read == 78 && buffer[0] == 0x31)
                        {
                            badCrcReports++;
                            if (detail != "Bluetooth extended report rejected: invalid CRC")
                            {
                                detail = "Bluetooth extended report rejected: invalid CRC";
                                Publish(Snapshot(detail));
                            }
                        }
                        continue;
                    }

                    awaitingExtended = false;
                    detail = "Bluetooth input active";
                    psDown = input.PsPressed;
                    batteryChanged = battery != input.Battery;
                    battery = input.Battery;
                    batteryStatusRaw = input.RawBatteryStatus;
                    btCandidateChanged = btCandidateObserver.Observe(input.EdgeProfileCandidate);
                    profileCandidate = btCandidateObserver.Current;
                    profileChanged = debounce.Observe(EdgeProfileDecoder.FromCode(input.EdgeProfileCandidate));
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
                lastInput = DateTimeOffset.UtcNow;
                Interlocked.Exchange(ref lastReportTicks, DateTime.UtcNow.Ticks);
                if (psButton.Observe(psDown, DateTimeOffset.UtcNow))
                    PsPressed?.Invoke(battery);

                if (profileChanged || batteryChanged || btCandidateChanged || reports == 1)
                    Publish(Snapshot(detail));
            }
        }
        catch (Exception ex) when (token.IsCancellationRequested &&
            ex is IOException or ObjectDisposedException or OperationCanceledException) { }
        finally { Publish(EdgeStatus.Disconnected with { ScanSummary = scanSummary }); }
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
