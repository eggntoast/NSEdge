using System.IO.MemoryMappedFiles;
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
