using System.IO;
using System.IO.MemoryMappedFiles;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using NSEdge.Core;

namespace NSEdge.App.Services;

internal static class Ds4WindowsIpc
{
    private const uint WmCopyData = 0x004A;
    private const uint SmtoAbortIfHung = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct CopyData
    {
        public nint Data;
        public int Length;
        public nint Text;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string className, string windowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SendMessageTimeout(nint window, uint message, nint wParam,
        ref CopyData data, uint flags, uint timeoutMs, out nint result);

    private static nint FindHost()
    {
        using var classMap = MemoryMappedFile.OpenExisting("DS4Windows_IPCClassName.dat");
        using var view = classMap.CreateViewAccessor(0, 128, MemoryMappedFileAccess.Read);
        byte[] buffer = new byte[128];
        view.ReadArray(0, buffer, 0, buffer.Length);
        int end = Array.IndexOf(buffer, (byte)0);
        string className = Encoding.ASCII.GetString(buffer, 0, end < 0 ? buffer.Length : end);
        if (string.IsNullOrWhiteSpace(className))
            throw new InvalidOperationException("DS4Windows IPC class is unavailable.");
        nint host = FindWindow(className, "DS4Windows");
        if (host == 0)
            throw new InvalidOperationException("No compatible running DS4Windows IPC host was found.");
        return host;
    }

    private static void Send(nint host, string command)
    {
        nint message = Marshal.StringToHGlobalAnsi(command);
        try
        {
            var data = new CopyData { Data = 0, Length = Encoding.ASCII.GetByteCount(command), Text = message };
            if (SendMessageTimeout(host, WmCopyData, 0, ref data, SmtoAbortIfHung, 2000, out _) == 0)
                throw new IOException("DS4Windows did not accept the IPC command.");
        }
        finally { Marshal.FreeHGlobal(message); }
    }

    private static string Query(nint host, int slot, string property)
    {
        using var mutex = new Mutex(false, "DS4Windows_IPCResultData_SingleTaskMtx");
        bool acquired;
        try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(2)); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired)
            throw new IOException("DS4Windows IPC is busy.");
        try
        {
            using var map = MemoryMappedFile.CreateNew("DS4Windows_IPCResultData.dat", 256);
            using var view = map.CreateViewAccessor();
            using var ready = new EventWaitHandle(false, EventResetMode.AutoReset,
                "DS4Windows_IPCResultData_ReadyEvent");
            Send(host, $"Query.{slot}.{property}");
            if (!ready.WaitOne(TimeSpan.FromSeconds(2)))
                throw new IOException("DS4Windows IPC query timed out.");
            byte[] data = new byte[256];
            view.ReadArray(0, data, 0, data.Length);
            int end = Array.IndexOf(data, (byte)0);
            return Encoding.ASCII.GetString(data, 0, end < 0 ? data.Length : end).Trim();
        }
        finally { mutex.ReleaseMutex(); }
    }


    // Fingerprint of the published Ryochan7 3.3.3 DLL, not a user-specific controller value.
    private const string Known333DllSha256 =
        "D89C4D3D0F45187283A2D71FF22623D0F871D59A34754065A81EA98C7A6E1FDA";

    /// <summary>
    /// Sends ONLY stock DS4Windows Query messages. No changeledcolor, disconnect,
    /// profile loading, native HID output, controller writes or persistent settings changes.
    /// Observations are advisory; they do not authorize future hardware output.
    /// </summary>
    public static Ds4ReadOnlyProbeResult ProbeReadOnly(string? identity, EdgeTransport? expectedTransport)
    {
        if (ControllerIdentity.Normalize(identity) is not string expected || expectedTransport is null)
            return new("NSEdge controller identity or transport unavailable", null, null, null, null, false);

        try
        {
            nint host = FindHost();
            if (GetWindowThreadProcessId(host, out uint ownerPid) == 0 || ownerPid == 0)
                return new("Could not identify the DS4Windows window owner", null, null, null, null, false);
            using Process process = Process.GetProcessById(checked((int)ownerPid));
            if (!string.Equals(process.ProcessName, "DS4Windows", StringComparison.OrdinalIgnoreCase))
                return new("IPC window is not owned by DS4Windows", process.Id, null, null, null, false);

            // Identify the expected compatible executable; this is not process authentication.
            string? executable = process.MainModule?.FileName;
            if (executable is null || !string.Equals(Path.GetFileName(executable), "DS4Windows.exe",
                    StringComparison.OrdinalIgnoreCase))
                return new("Could not verify the running executable", process.Id, null, null, null, false);

            DateTime started = process.StartTime;
            FileVersionInfo version = FileVersionInfo.GetVersionInfo(executable);
            string? productVersion = version.ProductVersion;
            string assembly = Path.Combine(Path.GetDirectoryName(executable)!, "DS4Windows.dll");
            if (!File.Exists(assembly))
                return new("DS4Windows.dll not found beside the running executable", process.Id, productVersion, null, null, false);

            using var stream = new FileStream(assembly, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            string hash = Convert.ToHexString(SHA256.HashData(stream));
            bool recognized = string.Equals(hash, Known333DllSha256, StringComparison.OrdinalIgnoreCase)
                && string.Equals(version.FileVersion, "3.3.3", StringComparison.Ordinal);

            if (!recognized)
                return new("Unrecognized DS4Windows build — no IPC queries sent",
                    process.Id, productVersion, null, null, false);

            // Source-confirmed Ryochan7 3.3.3 maximum is eight physical slots.
            // All queries are read-only; a failed query cannot authorize future output.
            bool HostUnchanged() =>
                GetWindowThreadProcessId(host, out uint currentPid) != 0 &&
                currentPid == ownerPid && !process.HasExited && process.StartTime == started;

            var budget = Stopwatch.StartNew();
            Ds4ProbeScan scan = Ds4ReadOnlyProbeLogic.Scan(expected, expectedTransport,
                (slot, property) => Ds4QueryTransport.Query(host, slot, property,
                    Ds4QueryNames.Production, TimeSpan.FromSeconds(8) - budget.Elapsed, HostUnchanged),
                HostUnchanged);

            if (!HostUnchanged())
                return new("DS4Windows restarted during the probe", null, productVersion, null, null, true);

            return scan.IsMatched
                ? new("Controller matched (read-only; no lighting control)", process.Id,
                    productVersion, scan.Controller!.Slot,
                    scan.Controller.Profile, true, scan.Controller.Transport,
                    scan.EmptyAddressSlots, scan.MalformedAddressSlots)
                : new(scan.Code switch
                    {
                        Ds4ProbeCode.Inconclusive => "No match observed; empty slots or incomplete stock IPC responses",
                        Ds4ProbeCode.IncompleteResponse => "Matching identity returned incomplete transport/profile data",
                        _ => "Controller not matched: " + scan.Code
                    }, process.Id, productVersion, null, null, true, null,
                    scan.EmptyAddressSlots, scan.MalformedAddressSlots);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception or
            System.Threading.WaitHandleCannotBeOpenedException)
        {
            return new("DS4Windows read-only probe unavailable: " + ex.GetType().Name,
                null, null, null, null, false);
        }
    }

    public static string DisconnectMatchingBluetoothController(string identity)
    {
        string? target = ControllerIdentity.Normalize(identity);
        if (target is null)
            throw new InvalidOperationException("Controller identity is unavailable.");

        nint host = FindHost();
        int slot = 0;
        for (int candidate = 1; candidate <= 4; candidate++)
        {
            string address = Query(host, candidate, "MacAddress");
            if (ControllerIdentity.Normalize(address) != target) continue;
            string transport = Query(host, candidate, "ConnType");
            if (!Ds4ControllerTarget.IsMatch(target, address, transport))
                throw new InvalidOperationException("DS4Windows reports this controller is not connected via Bluetooth.");
            if (slot != 0)
                throw new InvalidOperationException("DS4Windows returned duplicate matching controllers.");
            slot = candidate;
        }
        if (slot == 0)
            throw new InvalidOperationException("Your Bluetooth controller could not be matched to a DS4Windows slot.");

        // Check again immediately before issuing the state-changing command.
        if (!Ds4ControllerTarget.IsMatch(target, Query(host, slot, "MacAddress"),
                Query(host, slot, "ConnType")))
            throw new InvalidOperationException("Controller selection changed. No disconnect was sent.");

        Send(host, $"Disconnect.{slot}");
        return $"Sent a disconnect request to DS4Windows for controller slot {slot}.";
    }
}

internal sealed record Ds4ReadOnlyProbeResult(
    string Status, int? ProcessId, string? ProductVersion,
    int? ControllerSlot, string? SoftwareProfile, bool RecognizedBinary,
    string? ReportedTransport = null, int EmptyAddressSlots = 0, int MalformedAddressSlots = 0)
{
    public string Summary =>
        $"Status: {Status}\n" +
        $"DS4Windows process: {(ProcessId?.ToString() ?? "Unavailable")}\n" +
        $"Version: {ProductVersion ?? "Unavailable"}\n" +
        $"Recognized 3.3.3 DLL: {RecognizedBinary}\n" +
        $"Matching slot: {(ControllerSlot?.ToString() ?? "None")}\n" +
        $"DS4Windows reported transport: {ReportedTransport ?? "Unavailable"}\n" +
        $"DS4Windows software profile: {SoftwareProfile ?? "Unavailable"}\n" +
        $"Empty address replies: {EmptyAddressSlots}; malformed addresses skipped: {MalformedAddressSlots}\n" +
        "Stock empty replies cannot distinguish vacant slots from failed response writes.\n" +
        "Temporary RGB ownership: not supported by stock DS4Windows 3.3.3";
}