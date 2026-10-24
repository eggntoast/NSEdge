using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;

namespace NSEdge.App.Services;

/// <summary>
/// IPC object names can be isolated for tests. Production names are the stock
/// Ryochan7 3.3.3 result channel; never use them in the fake-host tests.
/// </summary>
internal sealed record Ds4QueryNames(string Mutex, string ResultMap, string ReadyEvent)
{
    internal static readonly Ds4QueryNames Production = new(
        "DS4Windows_IPCResultData_SingleTaskMtx",
        "DS4Windows_IPCResultData.dat",
        "DS4Windows_IPCResultData_ReadyEvent");
}

/// <summary>
/// Bounded, read-only DS4Windows Query IPC. No native controller API, no write commands.
/// The fake Windows host tests this exact transport with private test-only object names.
/// </summary>
internal static class Ds4QueryTransport
{
    private const uint WmCopyData = 0x004A;
    private const uint SmtoAbortIfHung = 0x0002;
    private const int ResultSize = 256;

    [StructLayout(LayoutKind.Sequential)]
    private struct CopyData
    {
        public nint Data;
        public int Length;
        public nint Text;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SendMessageTimeout(nint window, uint message, nint wParam,
        ref CopyData data, uint flags, uint timeoutMs, out nint result);

    internal static string Query(nint window, int slot, string property,
        Ds4QueryNames names, TimeSpan budget, Func<bool>? hostStable = null)
    {
        if (window == 0) throw new ArgumentException("Invalid IPC window.", nameof(window));
        if (slot is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(slot));
        if (property is not ("MacAddress" or "ConnType" or "ProfileName"))
            throw new ArgumentException("Only read-only controller properties are allowed.", nameof(property));
        ArgumentNullException.ThrowIfNull(names);
        if (budget <= TimeSpan.Zero) throw new TimeoutException("Read-only IPC budget exhausted.");

        var elapsed = Stopwatch.StartNew();
        bool Stable() => hostStable?.Invoke() ?? true;
        int RemainingMillis()
        {
            if (!Stable()) throw new IOException("DS4Windows IPC host changed.");
            var remaining = budget - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero) throw new TimeoutException("Read-only IPC budget exhausted.");
            return Math.Max(1, (int)Math.Ceiling(Math.Min(remaining.TotalMilliseconds, 2000)));
        }

        using var mutex = new Mutex(false, names.Mutex);
        bool acquired;
        try { acquired = mutex.WaitOne(RemainingMillis()); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new TimeoutException("DS4Windows query mutex timed out.");

        try
        {
            using var map = MemoryMappedFile.CreateNew(names.ResultMap, ResultSize);
            using var view = map.CreateViewAccessor(0, ResultSize);
            using var ready = new EventWaitHandle(false, EventResetMode.AutoReset, names.ReadyEvent);
            // A named event may survive while another process holds it. Clear a stale signal
            // under the query mutex; it cannot count as this request's acknowledgment.
            ready.Reset();
            if (!Stable()) throw new IOException("DS4Windows IPC host changed.");

            string command = $"Query.{slot}.{property}";
            nint ptr = Marshal.StringToHGlobalAnsi(command);
            try
            {
                var data = new CopyData { Data = 0, Length = Encoding.ASCII.GetByteCount(command), Text = ptr };
                if (SendMessageTimeout(window, WmCopyData, 0, ref data,
                        SmtoAbortIfHung, (uint)RemainingMillis(), out _) == 0)
                    throw new IOException("DS4Windows IPC query was not delivered.");
            }
            finally { Marshal.FreeHGlobal(ptr); }

            // Poll with bounded waits so a replaced/closed IPC host is detected
            // without waiting for the whole query timeout. A reply still needs the
            // named event from this request, not merely successful WM_COPYDATA delivery.
            while (true)
            {
                if (!Stable()) throw new IOException("DS4Windows IPC host changed.");
                if (ready.WaitOne(Math.Min(50, RemainingMillis()))) break;
            }
            if (!Stable()) throw new IOException("DS4Windows IPC host changed.");

            byte[] response = new byte[ResultSize];
            view.ReadArray(0, response, 0, response.Length);
            int terminator = Array.IndexOf(response, (byte)0);
            if (terminator < 0) throw new InvalidDataException("Unterminated DS4Windows IPC response.");
            // Do not silently replace non-ASCII bytes with '?' and mistake them for valid data.
            if (response.AsSpan(0, terminator).IndexOfAnyInRange((byte)128, byte.MaxValue) >= 0)
                throw new InvalidDataException("Non-ASCII DS4Windows IPC response.");
            return Encoding.ASCII.GetString(response, 0, terminator).Trim();
        }
        finally { mutex.ReleaseMutex(); }
    }
}
