namespace NSEdge.Core;

public enum EdgeProfile
{
    Unknown,
    Default,
    Circle,
    Cross
}

public static class EdgeProfileDecoder
{
    public const ushort VendorId = 0x054C;
    public const ushort ProductId = 0x0DF2;
    public const int ProfileByteIndex = 49;
    public const int MinimumReportLength = 50;

    public static EdgeProfile Decode(ReadOnlySpan<byte> report)
    {
        if (report.Length < MinimumReportLength || report[0] != 0x01)
            return EdgeProfile.Unknown;

        return FromCode(report[ProfileByteIndex]);
    }

    public static EdgeProfile FromCode(byte code) => code switch
    {
        0x1C => EdgeProfile.Default,
        0x4C => EdgeProfile.Circle,
        0x3C => EdgeProfile.Cross,
        _ => EdgeProfile.Unknown
    };

    public static string Shortcut(EdgeProfile profile) => profile switch
    {
        EdgeProfile.Default => "Fn + Triangle",
        EdgeProfile.Circle => "Fn + Circle",
        EdgeProfile.Cross => "Fn + Cross",
        _ => "Unknown"
    };
}

public sealed class ProfileDebouncer(int requiredReports = 12)
{
    private EdgeProfile candidate;
    private int consecutive;

    public EdgeProfile Current { get; private set; }

    public bool Observe(EdgeProfile next)
    {
        if (candidate != next)
        {
            candidate = next;
            consecutive = 1;
        }
        else if (consecutive < requiredReports)
        {
            consecutive++;
        }

        if (consecutive < requiredReports || Current == next)
            return false;

        Current = next;
        return true;
    }
}

public sealed record ProfileNames(string? Circle, string? Cross, string? Square)
{
    public static readonly ProfileNames Empty = new(null, null, null);

    public string DisplayName(EdgeProfile profile) => profile switch
    {
        EdgeProfile.Default => "Default",
        EdgeProfile.Circle => string.IsNullOrWhiteSpace(Circle) ? "Circle profile" : Circle,
        EdgeProfile.Cross => string.IsNullOrWhiteSpace(Cross) ? "Cross profile" : Cross,
        _ => "Unknown"
    };
}
