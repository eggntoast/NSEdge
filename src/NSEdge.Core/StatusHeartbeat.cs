namespace NSEdge.Core;

// Publish live counters at most once per second when controller state is unchanged.
// Monotonic time keeps clock adjustments from stopping diagnostic updates.
public sealed class StatusHeartbeat(TimeProvider clock)
{
    private long published = clock.GetTimestamp();
    public bool IsDue => clock.GetElapsedTime(published) >= TimeSpan.FromSeconds(1);
    public void Published() => published = clock.GetTimestamp();
}