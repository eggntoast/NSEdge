using NSEdge.Windows.Hid;

namespace NSEdge.App.Services;

/// <summary>
/// Formats only selected, non-identifying controller facts. Never concatenate an
/// exception message, device path, controller address or user-defined profile name.
/// </summary>
internal static class DiagnosticsReport
{
    public static string Build(EdgeStatus captured, EdgeStatus current,
        string safeCompatibility, bool lightingRequested, bool inputFresh,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(captured);
        ArgumentNullException.ThrowIfNull(current);
        clock ??= TimeProvider.System;

        bool fresh = inputFresh && captured.IsSameConnectionAs(current);
        string compatibility = fresh ? safeCompatibility :
            "Discarded: controller connection changed or input is stale.";
        string profile = fresh ? current.Profile.ToString() : "Unavailable (input stale or session changed)";
        string battery = fresh ? current.Battery.Description : "Unavailable (input stale or session changed)";
        string Hex(byte? value) => fresh && value is byte b ? $"0x{b:X2}" : "Unavailable";

        return
            $"NSEdge diagnostics\n" +
            $"Captured (UTC): {clock.GetUtcNow():O}\n" +
            $"Connected: {current.Connected}; Transport: {current.Transport?.ToString() ?? "None"}\n" +
            $"Input fresh and same connection: {fresh}\n" +
            $"Profile: {profile}\n" +
            $"Saved Sony names available: Circle={current.Names.Circle is not null}, " +
                $"Cross={current.Names.Cross is not null}, Square={current.Names.Square is not null}\n" +
            $"Battery: {battery}\n" +
            $"Battery status byte: {Hex(current.BatteryStatusRaw)}\n" +
            $"Bluetooth profile candidate: {Hex(current.BluetoothProfileCandidate)}\n" +
            $"USB profile candidate: {Hex(current.UsbProfileCandidate)}\n" +
            $"Controller identity available: {current.DeviceIdentityAvailable}\n" +
            $"Names supplied by local cache: {current.NamesFromUsbCache}\n" +
            $"Native lighting requested: {lightingRequested} (physical output not verified)\n" +
            $"DS4Windows compatibility (read-only):\n{compatibility}\n" +
            $"Reports: {current.ReportCount}; BT minimal: {current.MinimalReports}; bad CRC: {current.BadCrcReports}\n" +
            $"Last valid input (UTC): {current.LastInputUtc?.ToString("O") ?? "None"}\n" +
            "Names, controller identities, local paths and free-form diagnostic messages are omitted.\n" +
            "This is a point-in-time report. Reopen Diagnostics to refresh.";
    }
}
