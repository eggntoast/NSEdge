namespace NSEdge.Core;

// The first known state on each connection is a baseline, not a switch.
public sealed class ProfileNotificationTracker
{
    private EdgeProfile lastKnown = EdgeProfile.Unknown;

    public bool Observe(bool connected, EdgeProfile profile)
    {
        if (!connected)
        {
            lastKnown = EdgeProfile.Unknown;
            return false;
        }

        if (profile == EdgeProfile.Unknown || profile == lastKnown)
            return false;

        bool changed = lastKnown != EdgeProfile.Unknown;
        lastKnown = profile;
        return changed;
    }
}
