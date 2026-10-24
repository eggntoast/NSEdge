namespace NSEdge.Core;

public enum Ds4ProbeCode
{
    Matched,
    IdentityUnavailable,
    ControllerNotFound,
    TransportMismatch,
    DuplicateIdentity,
    HostChanged,
    ChangedDuringProbe,
    MalformedResponse,
    QueryFailed
}

public sealed record Ds4ProbeScan(Ds4ProbeCode Code, Ds4ControllerObservation? Controller)
{
    public bool IsMatched => Code == Ds4ProbeCode.Matched && Controller is not null;
}

/// <summary>
/// Read-only interrogation of an injectable slot-query source. No operating-system or HID access.
/// In production the source may only expose the stock DS4Windows Query IPC operation.
/// </summary>
public static class Ds4ReadOnlyProbeLogic
{
    public const int MaxSlots333 = 8;

    public static Ds4ProbeScan Scan(string? expectedIdentity, EdgeTransport? expectedTransport,
        Func<int, string, string?> query, Func<bool> hostIsSame)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(hostIsSame);
        string? expected = ControllerIdentity.Normalize(expectedIdentity);
        if (expected is null || expectedTransport is null)
            return new(Ds4ProbeCode.IdentityUnavailable, null);

        bool Changed() => !hostIsSame();

        string? Read(int slot, string property)
        {
            if (Changed()) throw new HostChangedException();
            string? value = query(slot, property);
            if (Changed()) throw new HostChangedException();
            if (value is null || value.Length > 255 || value.Any(char.IsControl))
                throw new FormatException("Unexpected IPC response.");
            return value.Trim();
        }

        try
        {
            var observations = new List<Ds4ControllerObservation>();
            for (int slot = 1; slot <= MaxSlots333; slot++)
            {
                string? address = Read(slot, "MacAddress");
                if (string.IsNullOrEmpty(address)) continue;
                string? normalized = ControllerIdentity.Normalize(address);
                if (normalized is null) return new(Ds4ProbeCode.MalformedResponse, null);
                if (normalized != expected) continue;
                string? transport = Read(slot, "ConnType");
                string? profile = Read(slot, "ProfileName");
                observations.Add(new(slot, address, transport, profile));
            }

            Ds4Selection selection = Ds4ReadOnlySelection.Find(expected, expectedTransport, observations);
            if (selection.Status == Ds4SelectionStatus.DuplicateIdentity)
                return new(Ds4ProbeCode.DuplicateIdentity, null);
            if (selection.Status == Ds4SelectionStatus.TransportMismatch)
                return new(Ds4ProbeCode.TransportMismatch, null);
            if (!selection.IsMatched)
                return new(Ds4ProbeCode.ControllerNotFound, null);

            Ds4ControllerObservation found = selection.Controller!;
            // Three independent queries may have observed different controller generations.
            // Re-read all three fields and invalidate any inconsistent observation.
            if (ControllerIdentity.Normalize(Read(found.Slot, "MacAddress")) != expected ||
                !string.Equals(Read(found.Slot, "ConnType"), found.Transport, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Read(found.Slot, "ProfileName"), found.Profile, StringComparison.Ordinal))
                return new(Ds4ProbeCode.ChangedDuringProbe, null);

            return Changed()
                ? new(Ds4ProbeCode.HostChanged, null)
                : new(Ds4ProbeCode.Matched, found);
        }
        catch (HostChangedException)
        {
            return new(Ds4ProbeCode.HostChanged, null);
        }
        catch (FormatException)
        {
            return new(Ds4ProbeCode.MalformedResponse, null);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or
            System.Threading.WaitHandleCannotBeOpenedException or InvalidOperationException)
        {
            return new(Ds4ProbeCode.QueryFailed, null);
        }
    }

    private sealed class HostChangedException : Exception { }
}
