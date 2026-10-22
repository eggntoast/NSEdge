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
Test("Repeated reports do not retrigger a notification", () =>
{
    var debounce = new ProfileDebouncer();
    for (int i = 0; i < 12; i++) debounce.Observe(EdgeProfile.Default);
    for (int i = 0; i < 200; i++) Check(!debounce.Observe(EdgeProfile.Default));
    for (int i = 0; i < 11; i++) Check(!debounce.Observe(EdgeProfile.Circle));
    Check(debounce.Observe(EdgeProfile.Circle));
});
Test("Name decoder rejects corrupted trailing data", () =>
{
    var (first, second) = NameReports("DarkSouls", 0);
    first[6 + ("DarkSouls".Length + 1) * 2] = (byte)'X';
    Check(ProfileNameDecoder.Decode(first, second) is null);
});
Test("USB battery bands and charging states", () =>
{
    Check(EdgeInputDecoder.TryDecode(BatteryReport(0x04), out var low, out _));
    Check(low == new EdgeBattery(4, EdgeChargeState.Discharging));
    Check(low.LevelText == "40–49%");
    Check(EdgeInputDecoder.TryDecode(BatteryReport(0x13), out var charging, out _));
    Check(charging == new EdgeBattery(3, EdgeChargeState.Charging));
    Check(EdgeInputDecoder.TryDecode(BatteryReport(0x20), out var full, out _));
    Check(full == new EdgeBattery(10, EdgeChargeState.Full));
    Check(EdgeInputDecoder.TryDecode(BatteryReport(0xA5), out var overheat, out _));
    Check(overheat.Decile is null && overheat.Charge == EdgeChargeState.NotCharging);
    Check(EdgeInputDecoder.TryDecode(BatteryReport(0xF3), out var failure, out _));
    Check(failure.Decile is null && failure.Charge == EdgeChargeState.Error);
});
Test("Unknown and malformed battery reports do not invent percentages", () =>
{
    Check(!EdgeInputDecoder.TryDecode(new byte[53], out var invalid, out _));
    Check(invalid == EdgeBattery.Unavailable);
    byte[] wrongId = BatteryReport(0x08);
    wrongId[0] = 0x31;
    Check(!EdgeInputDecoder.TryDecode(wrongId, out _, out _));
    Check(EdgeInputDecoder.TryDecode(BatteryReport(0x0F), out var impossible, out _));
    Check(impossible == EdgeBattery.Unavailable);
    Check(EdgeInputDecoder.TryDecode(BatteryReport(0xE8), out var unknownCharge, out _));
    Check(unknownCharge == EdgeBattery.Unavailable);
});
Test("PS button reads USB byte 10 bit zero", () =>
{
    byte[] report = BatteryReport(0x03);
    Check(EdgeInputDecoder.TryDecode(report, out _, out bool pressed) && !pressed);
    report[10] = 0x01;
    Check(EdgeInputDecoder.TryDecode(report, out _, out pressed) && pressed);
    report[10] = 0x02;
    Check(EdgeInputDecoder.TryDecode(report, out _, out pressed) && !pressed);
});
Test("PS press emits one event per valid rise", () =>
{
    var detector = new PsPressDetector();
    var time = DateTimeOffset.UnixEpoch;
    Check(!detector.Observe(true, time)); // Ignore state already down at startup.
    Check(!detector.Observe(true, time.AddMilliseconds(25)));
    Check(!detector.Observe(false, time.AddMilliseconds(50)));
    Check(detector.Observe(true, time.AddMilliseconds(400)));
    Check(!detector.Observe(true, time.AddMilliseconds(401)));
    Check(!detector.Observe(false, time.AddMilliseconds(500)));
    Check(!detector.Observe(true, time.AddMilliseconds(550))); // Bounce within 300 ms.
    Check(!detector.Observe(false, time.AddMilliseconds(575)));
    Check(detector.Observe(true, time.AddMilliseconds(950)));
});
Test("Low battery warnings are edge-triggered and discharging only", () =>
{
    var policy = new LowBatteryPolicy();
    var normal = new EdgeBattery(3, EdgeChargeState.Discharging);
    var low = new EdgeBattery(1, EdgeChargeState.Discharging);
    Check(!policy.Observe(normal, 20));
    Check(policy.Observe(low, 20));
    Check(!policy.Observe(low, 20));
    Check(!policy.Observe(EdgeBattery.Unavailable, 20));
    Check(!policy.Observe(low, 20));
    Check(!policy.Observe(new EdgeBattery(1, EdgeChargeState.Charging), 20));
    Check(policy.Observe(low, 20));
    policy.Reset();
    Check(policy.Observe(low, 20));
});
Test("HID report length separates USB from Bluetooth", () =>
{
    Check(EdgeTransportDetector.FromInputReportLength(64) == EdgeTransport.Usb);
    Check(EdgeTransportDetector.FromInputReportLength(78) == EdgeTransport.Bluetooth);
    Check(EdgeTransportDetector.FromInputReportLength(65) is null);
    Check(EdgeTransportDetector.FromInputReportLength(0) is null);
});
Test("Bluetooth CRC known vector", () =>
{
    // Verified independently with IEEE CRC-32 of 0xA1 + report bytes 0..73.
    byte[] report = FullBluetoothReport();
    Check(BluetoothInputDecoder.CalculateInputCrc(report.AsSpan(0, 74)) == 0x03EFD7C0);
    Check(BluetoothInputDecoder.HasValidCrc(report));
    report[21] ^= 1;
    Check(!BluetoothInputDecoder.HasValidCrc(report));
});
Test("Bluetooth PS and battery use shifted common input fields", () =>
{
    byte[] report = FullBluetoothReport();
    Check(BluetoothInputDecoder.Decode(report, out BluetoothInput input) == BluetoothReportKind.Extended);
    Check(input.PsPressed);
    Check(input.Battery == new EdgeBattery(4, EdgeChargeState.Charging));
    Check(input.RawBatteryStatus == 0x14);
    Check(input.EdgeProfileCandidate == 0x4C);
    Check(EdgeProfileDecoder.Decode(report) == EdgeProfile.Unknown); // USB decoder must not accept BT
});
Test("Bluetooth rejects wrong CRC, truncated and wrong report IDs", () =>
{
    byte[] valid = FullBluetoothReport();
    valid[74] ^= 1;
    Check(BluetoothInputDecoder.Decode(valid, out _) == BluetoothReportKind.Invalid);
    Check(BluetoothInputDecoder.Decode(valid.AsSpan(0, 77), out _) == BluetoothReportKind.Invalid);
    valid = FullBluetoothReport();
    valid[0] = 0x22;
    Check(BluetoothInputDecoder.Decode(valid, out _) == BluetoothReportKind.Invalid);
    valid[0] = 0x01;
    Check(BluetoothInputDecoder.Decode(valid, out _) == BluetoothReportKind.Minimal);
    Check(BluetoothInputDecoder.Decode(Array.Empty<byte>(), out _) == BluetoothReportKind.Invalid);
});
Test("Unverified Bluetooth profile candidate requires stable repeated reports", () =>
{
    var detector = new StableByteObserver();
    for (int i = 0; i < 11; i++) Check(!detector.Observe(0x1C));
    Check(detector.Observe(0x1C) && detector.Current == 0x1C);
    for (int i = 0; i < 11; i++) Check(!detector.Observe(0x4C));
    Check(detector.Observe(0x4C) && detector.Current == 0x4C);
    Check(!detector.Observe(0x4C));
});
Test("Identify Edge HID collections by attributes", () =>
{
    var usb = new EdgeHidDescriptor(0x054C, 0x0DF2, 0x01, 0x05, 64);
    var bt = new EdgeHidDescriptor(0x054C, 0x0DF2, 0x01, 0x05, 78);
    Check(EdgeHidIdentity.Match(usb) == EdgeTransport.Usb);
    Check(EdgeHidIdentity.Match(bt) == EdgeTransport.Bluetooth);
    Check(EdgeHidIdentity.Match(bt with { VendorId = 0x1234 }) is null);
    Check(EdgeHidIdentity.Match(bt with { ProductId = 0x0CE6 }) is null);
    Check(EdgeHidIdentity.Match(bt with { UsagePage = 0xFF00 }) is null);
    Check(EdgeHidIdentity.Match(bt with { Usage = 0x01 }) is null);
    Check(EdgeHidIdentity.Match(bt with { InputReportLength = 65 }) is null);
    Check(EdgeHidIdentity.Match(usb with { Usage = 0x04 }) == EdgeTransport.Usb);
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

static byte[] BatteryReport(byte status)
{
    byte[] report = new byte[64];
    report[0] = 0x01;
    report[53] = status;
    return report;
}

static byte[] FullBluetoothReport()
{
    byte[] report = new byte[78];
    report[0] = 0x31;
    report[1] = 0x10;
    report[11] = 0x01;
    report[50] = 0x4C;
    report[54] = 0x14;
    report[74] = 0xC0;
    report[75] = 0xD7;
    report[76] = 0xEF;
    report[77] = 0x03;
    return report;
}
