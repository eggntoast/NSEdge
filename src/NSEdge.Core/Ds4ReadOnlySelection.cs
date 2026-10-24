namespace NSEdge.Core;

public sealed record Ds4ControllerObservation(int Slot, string? Address, string? Transport, string? Profile);

public enum Ds4SelectionStatus
{
    Matched,
    ControllerIdentityUnavailable,
    ControllerNotFound,
    TransportMismatch,
    DuplicateIdentity
}

public sealed record Ds4Selection(Ds4SelectionStatus Status, Ds4ControllerObservation? Controller)
{
    public bool IsMatched => Status == Ds4SelectionStatus.Matched && Controller is not null;
}

/// <summary>Pure, fail-closed matching of read-only DS4Windows slot observations.</summary>
public static class Ds4ReadOnlySelection
{
    public static Ds4Selection Find(string? physicalIdentity, EdgeTransport? expectedTransport,
        IReadOnlyCollection<Ds4ControllerObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        string? expected = ControllerIdentity.Normalize(physicalIdentity);
        if (expected is null || expectedTransport is null)
            return new(Ds4SelectionStatus.ControllerIdentityUnavailable, null);

        var matches = observations.Where(slot =>
            ControllerIdentity.Normalize(slot.Address) == expected && slot.Slot > 0).ToArray();

        if (matches.Length == 0)
            return new(Ds4SelectionStatus.ControllerNotFound, null);
        // Never choose the first controller when the IPC provider returns ambiguous identities.
        if (matches.Length > 1)
            return new(Ds4SelectionStatus.DuplicateIdentity, null);

        var found = matches[0];
        bool transportMatches = expectedTransport.Value switch
        {
            EdgeTransport.Bluetooth => string.Equals(found.Transport?.Trim(), "BT", StringComparison.OrdinalIgnoreCase)
                || string.Equals(found.Transport?.Trim(), "Bluetooth", StringComparison.OrdinalIgnoreCase),
            EdgeTransport.Usb => string.Equals(found.Transport?.Trim(), "USB", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
        return transportMatches
            ? new(Ds4SelectionStatus.Matched, found)
            : new(Ds4SelectionStatus.TransportMismatch, null);
    }
}
