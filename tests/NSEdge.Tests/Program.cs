using NSEdge.Core;

int passed = 0;
Test("Known profile states", () =>
{
    Check(Decode(0x1C) == EdgeProfile.Default);
    Check(Decode(0x4C) == EdgeProfile.Circle);
    Check(Decode(0x3C) == EdgeProfile.Cross);
    Check(Decode(0x00) == EdgeProfile.Unknown);
    Check(Decode(0xFF) == EdgeProfile.Unknown);
});
Test("Reject malformed reports", () =>
{
    Check(EdgeProfileDecoder.Decode(Array.Empty<byte>()) == EdgeProfile.Unknown);
    Check(EdgeProfileDecoder.Decode(new byte[49]) == EdgeProfile.Unknown);
    byte[] report = Report(0x4C);
    report[0] = 0x31;
    Check(EdgeProfileDecoder.Decode(report) == EdgeProfile.Unknown);
});
Test("Debounce stable input", () =>
{
    var debounce = new ProfileDebouncer();
    for (int i = 0; i < 11; i++) Check(!debounce.Observe(EdgeProfile.Circle));
    Check(debounce.Observe(EdgeProfile.Circle));
    Check(!debounce.Observe(EdgeProfile.Circle));
    for (int i = 0; i < 11; i++) Check(!debounce.Observe(EdgeProfile.Unknown));
    Check(debounce.Observe(EdgeProfile.Unknown));
    Check(debounce.Current == EdgeProfile.Unknown);
    for (int i = 0; i < 11; i++) Check(!debounce.Observe(EdgeProfile.Cross));
    Check(debounce.Observe(EdgeProfile.Cross));
});
Test("Sony UTF-16LE name segments", () =>
{
    var (first, second) = NameReports("Silent Hill", 0);
    Check(ProfileNameDecoder.Decode(first, second) == "Silent Hill");
    (first, second) = NameReports("DarkSouls", 1);
    Check(ProfileNameDecoder.Decode(first, second) == "DarkSouls");
});
Test("Name spanning both feature reports", () =>
{
    string expected = new string('A', 27) + "Second";
    var (first, second) = NameReports(expected, 0);
    Check(ProfileNameDecoder.Decode(first, second) == expected);
});
Test("Unassigned and invalid name reports", () =>
{
    var (first, second) = NameReports("Empty", 0);
    first[1] = 0x10;
    Check(ProfileNameDecoder.Decode(first, second) == string.Empty);
    Check(ProfileNameDecoder.Decode(new byte[3], second) is null);
});
Test("Display name fallback", () =>
{
    var names = new ProfileNames("DarkSouls", "Silent Hill", null);
    Check(names.DisplayName(EdgeProfile.Circle) == "DarkSouls");
    Check(names.DisplayName(EdgeProfile.Cross) == "Silent Hill");
    Check(ProfileNames.Empty.DisplayName(EdgeProfile.Circle) == "Custom 1");
});
Console.WriteLine($"Passed {passed} protocol tests.");

void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { throw new Exception($"FAIL {name}: {ex.Message}", ex); }
}

static void Check(bool condition)
{
    if (!condition) throw new InvalidOperationException("Assertion failed");
}

static EdgeProfile Decode(byte code) => EdgeProfileDecoder.Decode(Report(code));

static byte[] Report(byte code)
{
    byte[] report = new byte[64];
    report[0] = 0x01;
    report[49] = code;
    return report;
}

static (byte[], byte[]) NameReports(string text, int shift)
{
    byte[] first = new byte[65];
    byte[] second = new byte[65];
    first[0] = 0x79;
    second[0] = 0x7A;
    for (int i = 0; i < text.Length; i++)
    {
        byte[] target = i < 27 ? first : second;
        int n = i < 27 ? i : i - 27;
        int offset = (i < 27 ? 6 : 2) + shift + n * 2;
        target[offset] = (byte)(text[i] & 0xFF);
        target[offset + 1] = (byte)(text[i] >> 8);
    }
    return (first, second);
}
