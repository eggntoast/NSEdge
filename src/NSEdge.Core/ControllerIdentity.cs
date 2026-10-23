namespace NSEdge.Core;

public static class ControllerIdentity
{
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string plain = value.Trim().Replace(":", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal);
        if (plain.Length != 12 || !plain.All(Uri.IsHexDigit)) return null;
        plain = plain.ToUpperInvariant();
        if (plain is "000000000000" or "FFFFFFFFFFFF") return null;
        return plain;
    }

    public static string? FromUsbAddress(ReadOnlySpan<byte> report)
    {
        if (report.Length < 7 || report[0] != 0x12) return null;
        Span<byte> address = stackalloc byte[6];
        for (int i = 0; i < 6; i++) address[i] = report[6 - i];
        return Normalize(Convert.ToHexString(address));
    }
}
