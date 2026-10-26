using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using NSEdge.App.Services;
using NSEdge.Core;

internal static class Program
{
    private static int passed;

    [STAThread]
    private static int Main()
    {
        try
        {
            Test("Actual WM_COPYDATA and named IPC round trip", RoundTrip);
            Test("No commands other than Query are accepted", ReadOnly);
            Test("Absent response times out under budget", Timeout);
            Test("Malformed response fails rather than matching a controller", Malformed);
            Test("Stale named event cannot fake a response", StaleEvent);
            Test("Mutex contention stays bounded", Contention);
            Test("Abandoned query mutex is recovered", Abandoned);
            Test("Closed host window fails safely", ClosedHost);
            Test("Query binds to the expected window PID", ProcessIdentity);
            Test("Host shutdown during IPC response fails closed", HostShutdown);
            Test("Hung fake IPC window respects send timeout", HungWindow);
            Test("Overall discovery budget prevents repeated long waits", OverallBudget);
            Test("Malformed unrelated address does not hide slot eight", MalformedUnrelatedSlot);
            Test("Empty stock replies cannot prove absence or complete matching", EmptyResponses);
            Test("Successful slow queries share the overall deadline", AccumulatingBudget);
            Console.WriteLine($"Passed {passed} Windows IPC integration tests.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void Test(string label, Action body)
    {
        body();
        passed++;
        Console.WriteLine($"PASS {label}");
    }
    private static void Require(bool value, string problem = "Assertion failed")
    {
        if (!value) throw new InvalidOperationException(problem);
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private static void RoundTrip()
    {
        using var host = new FakeQueryHost();
        host.Respond = c => c switch
        {
            "Query.1.MacAddress" => Bytes("AA1122334455"),
            "Query.1.ConnType" => Bytes("BT"),
            "Query.1.ProfileName" => Bytes("Default"),
            _ => Bytes("")
        };
        var data = Ds4ReadOnlyProbeLogic.Scan("AA1122334455", EdgeTransport.Bluetooth,
            (s, p) => Ds4QueryTransport.Query(host.Handle, s, p, host.Names,
                TimeSpan.FromSeconds(3), host.IsAlive),
            host.IsAlive);
        Require(data.IsMatched && data.Controller!.Slot == 1 && data.Controller.Profile == "Default");
        Require(host.Commands.All(x => x.StartsWith("Query.", StringComparison.Ordinal)));
        Require(host.Commands.Count >= 11);
    }

    private static void ReadOnly()
    {
        using var host = new FakeQueryHost();
        host.Respond = _ => Bytes("");
        Throws<ArgumentException>(() =>
            Ds4QueryTransport.Query(host.Handle, 1, "changeledcolor", host.Names, TimeSpan.FromSeconds(1)));
        Throws<ArgumentOutOfRangeException>(() =>
            Ds4QueryTransport.Query(host.Handle, 9, "MacAddress", host.Names, TimeSpan.FromSeconds(1)));
        Require(host.Commands.Count == 0);
    }

    private static void Timeout()
    {
        using var host = new FakeQueryHost();
        host.Respond = _ => null; // IPC message delivered; no response-event signal
        var timer = System.Diagnostics.Stopwatch.StartNew();
        Throws<TimeoutException>(() =>
            Ds4QueryTransport.Query(host.Handle, 1, "MacAddress", host.Names, TimeSpan.FromMilliseconds(180)));
        Require(timer.Elapsed < TimeSpan.FromSeconds(2));
    }

    private static void Malformed()
    {
        using var host = new FakeQueryHost();
        host.Respond = _ => Enumerable.Repeat((byte)'A', 256).ToArray();
        Throws<InvalidDataException>(() =>
            Ds4QueryTransport.Query(host.Handle, 1, "MacAddress", host.Names, TimeSpan.FromSeconds(2)));
        host.Respond = _ => new byte[] { 0xFF, 0x00 };
        Throws<InvalidDataException>(() =>
            Ds4QueryTransport.Query(host.Handle, 1, "MacAddress", host.Names, TimeSpan.FromSeconds(2)));
    }

    private static void StaleEvent()
    {
        using var host = new FakeQueryHost();
        host.Respond = _ => null;
        using var stale = new EventWaitHandle(true, EventResetMode.AutoReset, host.Names.ReadyEvent);
        Throws<TimeoutException>(() =>
            Ds4QueryTransport.Query(host.Handle, 1, "MacAddress", host.Names, TimeSpan.FromMilliseconds(200)));
    }

    private static void Contention()
    {
        using var host = new FakeQueryHost();
        host.Respond = _ => Bytes("AA1122334455");
        using var lockOwner = new Mutex(false, host.Names.Mutex);
        Require(lockOwner.WaitOne(1000));
        try
        {
            var task = Task.Run(() =>
            {
                Throws<TimeoutException>(() =>
                    Ds4QueryTransport.Query(host.Handle, 1, "MacAddress", host.Names,
                        TimeSpan.FromMilliseconds(150)));
            });
            Require(task.Wait(TimeSpan.FromSeconds(2)));
        }
        finally { lockOwner.ReleaseMutex(); }
    }

    private static void Abandoned()
    {
        using var host = new FakeQueryHost();
        host.Respond = _ => Bytes("AA1122334455");
        using var liveMutex = new Mutex(false, host.Names.Mutex);
        using var acquired = new ManualResetEventSlim(false);
        Thread t = new(() =>
        {
            using var abandoned = new Mutex(false, host.Names.Mutex);
            abandoned.WaitOne();
            acquired.Set();
            // Intentionally finish the thread without ReleaseMutex for abandoned-owner handling.
        }) { IsBackground = true };
        t.Start();
        Require(acquired.Wait(TimeSpan.FromSeconds(2)));
        Require(t.Join(2000));
        Require(Ds4QueryTransport.Query(host.Handle, 1, "MacAddress", host.Names,
            TimeSpan.FromSeconds(2)) == "AA1122334455");
    }

    private static void ClosedHost()
    {
        using var host = new FakeQueryHost();
        nint window = host.Handle;
        host.CloseWindow();
        Throws<IOException>(() =>
            Ds4QueryTransport.Query(window, 1, "MacAddress", host.Names, TimeSpan.FromMilliseconds(180)));
    }

    private static void ProcessIdentity()
    {
        using var host = new FakeQueryHost();
        uint threadId = GetWindowThreadProcessId(host.Handle, out uint processId);
        Require(threadId != 0 && processId == Environment.ProcessId);
        Require(host.IsAlive());
        host.CloseWindow();
        Require(!host.IsAlive());
    }

    private static void HostShutdown()
    {
        using var host = new FakeQueryHost();
        host.Respond = _ =>
        {
            host.ScheduleClose();
            return null;
        };
        Throws<IOException>(() =>
            Ds4QueryTransport.Query(host.Handle, 1, "MacAddress", host.Names,
                TimeSpan.FromSeconds(1), host.IsAlive));
        Require(!host.IsAlive());
    }

    private static void HungWindow()
    {
        using var host = new FakeQueryHost();
        host.Respond = _ =>
        {
            Thread.Sleep(400); // Simulates an unresponsive IPC message handler
            return null;
        };
        var timer = System.Diagnostics.Stopwatch.StartNew();
        Throws<IOException>(() =>
            Ds4QueryTransport.Query(host.Handle, 1, "MacAddress", host.Names,
                TimeSpan.FromMilliseconds(150), host.IsAlive));
        Require(timer.Elapsed < TimeSpan.FromSeconds(2));
    }

    private static void OverallBudget()
    {
        using var host = new FakeQueryHost();
        host.Respond = _ => null;
        var time = System.Diagnostics.Stopwatch.StartNew();
        var budget = System.Diagnostics.Stopwatch.StartNew();
        Ds4ProbeScan scan = Ds4ReadOnlyProbeLogic.Scan("AA1122334455",
            EdgeTransport.Bluetooth,
            (slot, property) => Ds4QueryTransport.Query(host.Handle, slot, property,
                host.Names, TimeSpan.FromMilliseconds(270) - budget.Elapsed, host.IsAlive),
            host.IsAlive);
        Require(scan.Code == Ds4ProbeCode.QueryFailed);
        Require(time.Elapsed < TimeSpan.FromSeconds(2));
    }

    private static byte[] Bytes(string value) => Encoding.ASCII.GetBytes(value + "\0");

    private static Ds4ProbeScan Scan(FakeQueryHost host) =>
        Ds4ReadOnlyProbeLogic.Scan("AA1122334455", EdgeTransport.Bluetooth,
            (slot, property) => Ds4QueryTransport.Query(host.Handle, slot, property,
                host.Names, TimeSpan.FromSeconds(2), host.IsAlive), host.IsAlive);

    private static void MalformedUnrelatedSlot()
    {
        using var host = new FakeQueryHost();
        host.Respond = command => Bytes(command switch
        {
            "Query.1.MacAddress" => "unavailable",
            "Query.8.MacAddress" => "AA1122334455",
            "Query.8.ConnType" => "BT",
            "Query.8.ProfileName" => "Default",
            _ => ""
        });
        var scan = Scan(host);
        Require(scan.IsMatched && scan.Controller!.Slot == 8);
        Require(scan.MalformedAddressSlots == 1 && scan.EmptyAddressSlots == 6);
    }

    private static void EmptyResponses()
    {
        using var host = new FakeQueryHost();
        // Zero-byte write plus event is exactly the stock empty response shape.
        host.Respond = _ => Array.Empty<byte>();
        var absent = Scan(host);
        Require(absent.Code == Ds4ProbeCode.Inconclusive && absent.EmptyAddressSlots == 8);
        foreach (string missing in new[] { "ConnType", "ProfileName" })
        {
            host.Respond = command => command == "Query.1." + missing ? Array.Empty<byte>() :
                Bytes(command switch
                {
                    "Query.1.MacAddress" => "AA1122334455",
                    "Query.1.ConnType" => "BT",
                    "Query.1.ProfileName" => "Default",
                    _ => ""
                });
            Require(Scan(host).Code == Ds4ProbeCode.IncompleteResponse);
        }
    }

    private static void AccumulatingBudget()
    {
        using var host = new FakeQueryHost();
        int replies = 0;
        host.Respond = _ =>
        {
            // Allow two completed round trips, then stop replying. No delayed writer
            // touches a disposed map after the client has exhausted its budget.
            if (Interlocked.Increment(ref replies) > 2) return null;
            Thread.Sleep(100);
            return Bytes("");
        };
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var scan = Ds4ReadOnlyProbeLogic.Scan("AA1122334455", EdgeTransport.Bluetooth,
            (slot, property) => Ds4QueryTransport.Query(host.Handle, slot, property, host.Names,
                TimeSpan.FromMilliseconds(600) - elapsed.Elapsed, host.IsAlive), host.IsAlive);
        Require(scan.Code == Ds4ProbeCode.QueryFailed && scan.EmptyAddressSlots == 2);
        Require(replies == 3 && elapsed.Elapsed < TimeSpan.FromSeconds(2));
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
}

internal sealed class FakeQueryHost : IDisposable
{
    private readonly Thread thread;
    private readonly ManualResetEventSlim ready = new(false);
    private FakeWindow? window;
    private bool closed;
    public readonly Ds4QueryNames Names;
    public nint Handle { get; private set; }
    public readonly System.Collections.Concurrent.ConcurrentQueue<string> Commands = new();
    public Func<string, byte[]?> Respond { get; set; } = _ => new byte[] { 0 };
    public bool IsAlive() => !closed && GetWindowThreadProcessId(Handle, out uint owner) != 0
        && owner == Environment.ProcessId;

    public FakeQueryHost()
    {
        var key = "NSEdge_IPC_Test_" + Guid.NewGuid().ToString("N");
        Names = new Ds4QueryNames(key + "_Mtx", key + "_Result", key + "_Ready");
        thread = new Thread(() =>
        {
            using var form = new FakeWindow(this)
            {
                ShowInTaskbar = false,
                Opacity = 0,
                WindowState = FormWindowState.Minimized
            };
            window = form;
            Handle = form.Handle;
            ready.Set();
            Application.Run(form);
            closed = true;
        }) { IsBackground = true, Name = "NSEdge harmless IPC fake" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!ready.Wait(TimeSpan.FromSeconds(5)) || Handle == 0)
            throw new InvalidOperationException("Fake IPC test window failed to start.");
    }

    public void ScheduleClose()
    {
        if (window is { IsHandleCreated: true } form)
            form.BeginInvoke(new Action(form.Close));
    }

    public void CloseWindow()
    {
        if (closed) return;
        if (window is { IsHandleCreated: true } form)
            form.BeginInvoke(new Action(form.Close));
        RequireExit();
    }
    private void RequireExit()
    {
        if (!thread.Join(3000))
            throw new InvalidOperationException("Fake IPC window did not close.");
    }
    public void Dispose()
    {
        CloseWindow();
        ready.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);

    private sealed class FakeWindow : Form
    {
        private readonly FakeQueryHost host;
        public FakeWindow(FakeQueryHost host) => this.host = host;

        [StructLayout(LayoutKind.Sequential)]
        private struct CopyData
        {
            public nint Data;
            public int Length;
            public nint Text;
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x004A)
            {
                var data = Marshal.PtrToStructure<CopyData>(message.LParam);
                if (data.Length < 0 || data.Length > 128)
                    throw new InvalidDataException("Fake received invalid command length.");
                string cmd = Marshal.PtrToStringAnsi(data.Text, data.Length) ?? "";
                host.Commands.Enqueue(cmd);
                byte[]? answer = host.Respond(cmd);
                if (answer is not null)
                {
                    using var map = MemoryMappedFile.OpenExisting(host.Names.ResultMap);
                    using var view = map.CreateViewAccessor();
                    if (answer.Length > 256) throw new InvalidDataException("Fake response exceeds shared memory.");
                    view.WriteArray(0, answer, 0, answer.Length);
                    using var signal = EventWaitHandle.OpenExisting(host.Names.ReadyEvent);
                    signal.Set();
                }
                message.Result = 1;
                return;
            }
            base.WndProc(ref message);
        }
    }
}
