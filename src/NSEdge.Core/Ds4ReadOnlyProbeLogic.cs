namespace NSEdge.Core;

public enum Ds4ProbeCode
{
    Matched,
    IdentityUnavailable,
    ControllerNotFound,
    Inconclusive,
    IncompleteResponse,
    TransportMismatch,
    DuplicateIdentity,
    HostChanged,
    ChangedDuringProbe,
    MalformedResponse,
    QueryFailed
}

public sealed record Ds4ProbeScan(Ds4ProbeCode Code, Ds4ControllerObservation? Controller,
    int EmptyAddressSlots = 0, int MalformedAddressSlots = 0)
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
        int emptySlots = 0;
        int malformedSlots = 0;
        Ds4ProbeScan Result(Ds4ProbeCode code, Ds4ControllerObservation? controller = null) =>
            new(code, controller, emptySlots, malformedSlots);

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
                if (string.IsNullOrEmpty(address)) { emptySlots++; continue; }
                string? normalized = ControllerIdentity.Normalize(address);
                // An unrelated unsupported controller must not hide a valid later slot.
                // Preserve the incomplete scan evidence; this is advisory discovery only.
                if (normalized is null) { malformedSlots++; continue; }
                if (normalized != expected) continue;
                string? transport = Read(slot, "ConnType");
                string? profile = Read(slot, "ProfileName");
                observations.Add(new(slot, address, transport, profile));
            }

            Ds4Selection selection = Ds4ReadOnlySelection.Find(expected, expectedTransport, observations);
            if (selection.Status == Ds4SelectionStatus.DuplicateIdentity)
                return Result(Ds4ProbeCode.DuplicateIdentity);
            if (observations.Any(o => string.IsNullOrEmpty(o.Transport) || string.IsNullOrEmpty(o.Profile)))
                return Result(Ds4ProbeCode.IncompleteResponse);
            if (selection.Status == Ds4SelectionStatus.TransportMismatch)
                return Result(Ds4ProbeCode.TransportMismatch);
            if (!selection.IsMatched)
                return Result(malformedSlots > 0 ? Ds4ProbeCode.MalformedResponse :
                    emptySlots > 0 ? Ds4ProbeCode.Inconclusive : Ds4ProbeCode.ControllerNotFound);

            Ds4ControllerObservation found = selection.Controller!;
            // Three independent queries may have observed different controller generations.
            // Re-read all three fields and invalidate any inconsistent observation.
            if (ControllerIdentity.Normalize(Read(found.Slot, "MacAddress")) != expected ||
                !string.Equals(Read(found.Slot, "ConnType"), found.Transport, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Read(found.Slot, "ProfileName"), found.Profile, StringComparison.Ordinal))
                return Result(Ds4ProbeCode.ChangedDuringProbe);

            return Changed()
                ? Result(Ds4ProbeCode.HostChanged)
                : Result(Ds4ProbeCode.Matched, found);
        }
        catch (HostChangedException)
        {
            return Result(Ds4ProbeCode.HostChanged);
        }
        catch (FormatException)
        {
            return Result(Ds4ProbeCode.MalformedResponse);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or
            System.Threading.WaitHandleCannotBeOpenedException or InvalidOperationException)
        {
            return Result(Ds4ProbeCode.QueryFailed);
        }
    }

    private sealed class HostChangedException : Exception { }
}