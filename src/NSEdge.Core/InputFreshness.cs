namespace NSEdge.Core;

/// <summary>
/// A monotonically timed marker for valid controller input. Wall-clock corrections
/// must never turn stale or missing HID reports into apparently fresh input.
/// </summary>
public sealed class InputFreshness
{
    private readonly TimeProvider clock;
    private readonly TimeSpan timeout;
    private long lastReportTimestamp;
    private int observed;

    public InputFreshness(TimeProvider clock, TimeSpan timeout)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        this.timeout = timeout;
    }

    public bool IsFresh
    {
        get
        {
            if (Volatile.Read(ref observed) == 0) return false;
            long stamp = Interlocked.Read(ref lastReportTimestamp);
            TimeSpan elapsed = clock.GetElapsedTime(stamp, clock.GetTimestamp());
            return elapsed >= TimeSpan.Zero && elapsed < timeout;
        }
    }

    public void Observe()
    {
        Interlocked.Exchange(ref lastReportTimestamp, clock.GetTimestamp());
        Volatile.Write(ref observed, 1);
    }

    public void Reset() => Volatile.Write(ref observed, 0);
}
