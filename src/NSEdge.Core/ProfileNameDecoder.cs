using System.Text;

namespace NSEdge.Core;

public static class ProfileNameDecoder
{
    // The name occupies 27 UTF-16LE code units in the first feature report
    // and 13 in the second. HID report-ID handling can shift these offsets.
    public static string? Decode(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        if (first.Length < 61 || second.Length < 29)
            return null;

        if (first[1] == 0x10 || first[2] == 0x10)
            return string.Empty;

        var zero = DecodeAt(first, second, 0);
        var one = DecodeAt(first, second, 1);
        if (zero is null)
            return one;
        if (one is null)
            return zero;

        // Preserve the alignment selection validated by the USB prototype.
        // Non-Latin-only labels cannot always be disambiguated with this heuristic.
        int latinZero = CountBasicLatin(zero);
        int latinOne = CountBasicLatin(one);
        if (latinZero == 0 && latinOne == 0)
            return null;
        return latinZero >= latinOne ? zero : one;
    }

    private static string? DecodeAt(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, int shift)
    {
        var builder = new StringBuilder(40);
        bool terminated = false;
        for (int segment = 0; segment < 2; segment++)
        {
            var data = segment == 0 ? first : second;
            int start = (segment == 0 ? 6 : 2) + shift;
            int units = segment == 0 ? 27 : 13;
            for (int i = 0; i < units; i++)
            {
                int index = start + 2 * i;
                if (index + 1 >= data.Length)
                    return null;
                char c = (char)(data[index] | data[index + 1] << 8);
                if (c == '\0')
                {
                    terminated = true;
                    continue;
                }
                if (terminated || char.IsControl(c) || char.IsSurrogate(c) || c == '\uFFFD')
                    return null;
                builder.Append(c);
            }
        }

        string result = builder.ToString().Trim();
        return result.Length is > 0 and <= 40 ? result : null;
    }

    private static int CountBasicLatin(string name) => name.Count(c => c is >= ' ' and <= '~');
}
