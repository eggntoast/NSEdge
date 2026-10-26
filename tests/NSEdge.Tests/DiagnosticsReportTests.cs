using NSEdge.App.Services;
using NSEdge.Core;
using NSEdge.Windows.Hid;

internal static class DiagnosticsReportTests
{
    public static int Run()
    {
        int count = 0;
        var status = EdgeStatus.Disconnected with
        {
            Connected = true,
            ConnectionGeneration = 5,
            ControllerIdentity = "AA1122334455",
            DevicePath = @"\\private\device\path",
            Transport = EdgeTransport.Bluetooth,
            Profile = EdgeProfile.Circle,
            Names = new ProfileNames("Secret Circle", "Secret Cross", null),
            Battery = EdgeBattery.Unavailable,
            DeviceIdentityAvailable = true,
            ReportCount = 50,
            LastInputUtc = DateTimeOffset.Parse("2026-10-26T12:00:00Z")
        };
        const string safeCompat = "Recognized DLL: True; matching slot: 1; transport: BT";

        string report = DiagnosticsReport.Build(status, status, safeCompat, false, true);
        Check(report.Contains("Profile: Circle") && report.Contains(safeCompat),
            "Fresh, matching input exposes permitted controller facts");
        Check(!report.Contains("Secret Circle") && !report.Contains("Secret Cross") &&
            !report.Contains("AA1122334455") && !report.Contains("private") &&
            !report.Contains(@"\\private\device\path"),
            "Report never exposes personal profile names, serials or paths");
        Check(report.Contains("Native lighting requested: False"),
            "Lighting disabled is not reported as enabled");

        string stale = DiagnosticsReport.Build(status, status, safeCompat, false, false);
        Check(stale.Contains("Battery: Unavailable") &&
              stale.Contains("Profile: Unavailable") && !stale.Contains(safeCompat),
            "Stale input hides battery/profile state and discards matching evidence");
        string changed = DiagnosticsReport.Build(status, status with { ConnectionGeneration = 6 },
            safeCompat, false, true);
        Check(changed.Contains("Discarded: controller connection changed") &&
              !changed.Contains(safeCompat), "Connection change invalidates IPC result");

        return count;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            count++;
            Console.WriteLine("PASS " + name);
        }
    }
}
