using NSEdge.Core;
using NSEdge.Windows.Hid;

int passed = 0;
Test("Lightbar RGB input parsing", () =>
{
    Check(LightbarColor.TryParse("#12aBcF", out var color));
    Check(color == new LightbarColor(0x12, 0xAB, 0xCF));
    Check(color.Hex == "#12ABCF");
    foreach (var bad in new[] { null, "", "AABBCC", "#AABBCCDD", "#ZZBBCC", "#12 345" })
        Check(!LightbarColor.TryParse(bad, out _));
});
Test("Lightbar policy respects charging and missing data", () =>
{
    var palette = new LightbarPalette();
    Check(LightbarPolicy.Select(EdgeBattery.Unavailable, LightbarMode.BatteryBands, palette) is null);
    Check(LightbarPolicy.Select(new EdgeBattery(4, EdgeChargeState.Charging), LightbarMode.BatteryBands, palette) == palette.Charging);
    Check(LightbarPolicy.Select(new EdgeBattery(10, EdgeChargeState.Full), LightbarMode.BatteryBands, palette) == palette.Full);
    Check(LightbarPolicy.Select(new EdgeBattery(null, EdgeChargeState.Error), LightbarMode.BatteryBands, palette) is null);
    Check(LightbarPolicy.Select(EdgeBattery.Unavailable, LightbarMode.Static, palette) == palette.Static);
});
Test("Lightbar bands use discrete lower-bound percentages", () =>
{
    var palette = new LightbarPalette();
    for (int d = 0; d <= 10; d++)
    {
        var b = new EdgeBattery(d, EdgeChargeState.Discharging);
        var expected = d < 2 ? palette.Critical : d < 4 ? palette.Low : d < 7 ? palette.Medium : palette.High;
        Check(LightbarPolicy.Select(b, LightbarMode.BatteryBands, palette) == expected);
    }
    Check(!((palette with { LowBelowPercent = 10 }).HasValidThresholds));
});
Test("Lightbar gradient is bounded and deterministic", () =>
{
    var palette = new LightbarPalette();
    Check(LightbarPolicy.Select(new EdgeBattery(0, EdgeChargeState.Discharging), LightbarMode.BatteryGradient, palette) == palette.Critical);
    Check(LightbarPolicy.Select(new EdgeBattery(10, EdgeChargeState.Discharging), LightbarMode.BatteryGradient, palette) == palette.High);
    Check(LightbarColor.Blend(new(0, 0, 0), new(255, 255, 255), 0.5) == new LightbarColor(128, 128, 128));
});
Test("USB lightbar output only sets the colour flag", () =>
{
    var colour = new LightbarColor(0x14, 0x9A, 0xFE);
    byte[] report = DualSenseLightbarReport.BuildUsb(colour);
    Check(report.Length == 63 && report[0] == 0x02);
    Check(report[1] == 0 && report[2] == 0x04);
    Check(report[45] == 0x14 && report[46] == 0x9A && report[47] == 0xFE);
    for (int i = 1; i < report.Length; i++)
        if (i is not (2 or 45 or 46 or 47)) Check(report[i] == 0);
    Check(DualSenseLightbarReport.BuildUsb(colour, 64).Length == 64);
    bool threw = false;
    try { DualSenseLightbarReport.BuildUsb(colour, 48); }
    catch (ArgumentOutOfRangeException) { threw = true; }
    Check(threw);
});
Test("Bluetooth lightbar output frames and authenticates colour", () =>
{
    var colour = new LightbarColor(0x14, 0x9A, 0xFE);
    byte[] report = DualSenseLightbarReport.BuildBluetooth(colour, 3);
    Check(report.Length == 78 && report[0] == 0x31 && report[1] == 0x30 && report[2] == 0x10);
    Check(report[4] == 0x04 && report[47] == 0x14 && report[48] == 0x9A && report[49] == 0xFE);
    uint actual = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(report.AsSpan(74));
    Check(actual == DualSenseLightbarReport.CalculateOutputCrc(report.AsSpan(0, 74)));
    Check(actual == 0x2796F2F6u); // Independent IEEE CRC-32 of 0xA2 + 74 output bytes.
    report[48] ^= 1;
    Check(actual != DualSenseLightbarReport.CalculateOutputCrc(report.AsSpan(0, 74)));
});
Test("DS4Windows disconnect targeting is controller-specific", () =>
{
    Check(Ds4ControllerTarget.IsMatch("AA:11:22:33:44:55", "aa1122334455", "BT"));
    Check(Ds4ControllerTarget.IsMatch("AA1122334455", "aa-11-22-33-44-55", "Bluetooth"));
    Check(!Ds4ControllerTarget.IsMatch("AA1122334455", "AA1122334456", "BT"));
    Check(!Ds4ControllerTarget.IsMatch("AA1122334455", "AA1122334455", "USB"));
    Check(!Ds4ControllerTarget.IsMatch(null, "AA1122334455", "BT"));
    Check(!Ds4ControllerTarget.IsMatch("AA1122334455", "unavailable", "BT"));
});
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
    Check(ProfileNames.Empty.DisplayName(EdgeProfile.Circle) == "Circle profile");
    Check(ProfileNames.Empty.DisplayName(EdgeProfile.Square) == "Square profile");
    Check(EdgeProfileDecoder.Shortcut(EdgeProfile.Square) == "Fn + Square");
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
Test("Bluetooth values map through the shared Edge decoder", () =>
{
    foreach (var (code, expected) in new (byte, EdgeProfile)[] {
        (0x1C, EdgeProfile.Default), (0x4C, EdgeProfile.Circle),
        (0x3C, EdgeProfile.Cross), (0x00, EdgeProfile.Unknown), (0xFF, EdgeProfile.Unknown)
    })
    {
        Check(EdgeProfileDecoder.FromCode(code) == expected);
        Check(Decode(code) == expected);
        byte[] packet = FullBluetoothReport();
        packet[50] = code;
        uint crc = BluetoothInputDecoder.CalculateInputCrc(packet.AsSpan(0, 74));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(74, 4), crc);
        Check(BluetoothInputDecoder.Decode(packet, out BluetoothInput input) == BluetoothReportKind.Extended);
        Check(EdgeProfileDecoder.FromCode(input.EdgeProfileCandidate) == expected);
        packet[50] ^= 0x01;
        Check(BluetoothInputDecoder.Decode(packet, out _) == BluetoothReportKind.Invalid);
    }
});
Test("Observed Bluetooth code transitions are debounced once", () =>
{
    // Codes came from physical NSEdge logs, but packets are synthetic.
    var debounce = new ProfileDebouncer();
    var tracker = new ProfileNotificationTracker();
    bool initialized = false;
    foreach (byte code in new byte[] { 0x1C, 0x4C, 0x1C, 0x3C, 0x1C })
    {
        EdgeProfile next = EdgeProfileDecoder.FromCode(code);
        for (int i = 0; i < 11; i++)
        {
            Check(!debounce.Observe(next));
            Check(!tracker.Observe(true, debounce.Current));
        }
        Check(debounce.Observe(next));
        bool notify = tracker.Observe(true, debounce.Current);
        if (!initialized)
        {
            Check(!notify); // Connecting does not cause an automatic popup.
            initialized = true;
        }
        else Check(notify);
        for (int i = 0; i < 30; i++)
        {
            Check(!debounce.Observe(next));
            Check(!tracker.Observe(true, debounce.Current));
        }
    }
});
Test("Unknown and reconnect do not cause false profile alerts", () =>
{
    var tracker = new ProfileNotificationTracker();
    Check(!tracker.Observe(false, EdgeProfile.Unknown));
    Check(!tracker.Observe(true, EdgeProfile.Unknown));
    Check(!tracker.Observe(true, EdgeProfile.Default));
    Check(!tracker.Observe(true, EdgeProfile.Default));
    Check(tracker.Observe(true, EdgeProfile.Circle));
    Check(!tracker.Observe(true, EdgeProfile.Unknown));
    Check(!tracker.Observe(true, EdgeProfile.Circle));
    Check(tracker.Observe(true, EdgeProfile.Cross));
    Check(!tracker.Observe(false, EdgeProfile.Unknown));
    Check(!tracker.Observe(true, EdgeProfile.Cross));
    Check(tracker.Observe(true, EdgeProfile.Default));
});
Test("Controller identifiers must match across transports", () =>
{
    byte[] usb = new byte[20];
    usb[0] = 0x09;
    usb[1] = 0x55;
    usb[2] = 0x44;
    usb[3] = 0x33;
    usb[4] = 0x22;
    usb[5] = 0x11;
    usb[6] = 0xAA;
    Check(ControllerIdentity.FromUsbAddress(usb) == "AA1122334455");
    Check(ControllerIdentity.Normalize("aa:11:22:33:44:55") == "AA1122334455");
    Check(ControllerIdentity.Normalize("aa-11-22-33-44-55") == "AA1122334455");
    Check(ControllerIdentity.Normalize("AA1122334456") != ControllerIdentity.FromUsbAddress(usb));
    Check(ControllerIdentity.FromUsbAddress(new byte[] { 0x09, 0 }) is null);
    Check(ControllerIdentity.FromUsbAddress(new byte[20]) is null);
    byte[] wrongFeature = (byte[])usb.Clone();
    wrongFeature[0] = 0x12;
    Check(ControllerIdentity.FromUsbAddress(wrongFeature) is null);
    byte[] missingAddress = (byte[])usb.Clone();
    Array.Clear(missingAddress, 1, 6);
    Check(ControllerIdentity.FromUsbAddress(missingAddress) is null);
    Check(ControllerIdentity.Normalize("000000000000") is null);
    Check(ControllerIdentity.Normalize("FFFFFFFFFFFF") is null);
    Check(ControllerIdentity.Normalize("BT-PATH") is null);
});
Test("Sony 0x09 pairing identity names survive a transport switch", () =>
{
    byte[] pairing = new byte[20];
    pairing[0] = 0x09;
    byte[] address = { 0x55, 0x44, 0x33, 0x22, 0x11, 0xAA };
    address.CopyTo(pairing, 1);
    string? usbIdentity = ControllerIdentity.FromUsbAddress(pairing);
    string? btIdentity = ControllerIdentity.Normalize("aa:11:22:33:44:55");
    Check(usbIdentity is not null && usbIdentity == btIdentity);

    string directory = Path.Combine(Path.GetTempPath(), "NSEdge-identity-" + Guid.NewGuid().ToString("N"));
    try
    {
        var cache = new ProfileNameCache(directory);
        var realNames = new ProfileNames("DarkSouls", "Silent Hill", null);
        cache.Save(usbIdentity, realNames);
        Check(Directory.GetFiles(directory, "*.json").Length == 1);
        var reopened = new ProfileNameCache(directory);
        Check(reopened.Read(btIdentity) == realNames);
        Check(reopened.Read("AA1122334456") == ProfileNames.Empty);
    }
    finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
});
Test("USB name cache is restricted to the matching controller", () =>
{
    string folder = Path.Combine(Path.GetTempPath(), "NSEdge-cache-tests-" + Guid.NewGuid().ToString("N"));
    try
    {
        var cache = new ProfileNameCache(folder);
        var saved = new ProfileNames("DarkSouls", "Silent Hill", null);
        cache.Save("AA1122334455", saved);
        Check(cache.Read("aa:11:22:33:44:55") == saved);
        Check(cache.Read("AA1122334456") == ProfileNames.Empty);
        Check(cache.Read(null) == ProfileNames.Empty);
        Check(cache.Read("000000000000") == ProfileNames.Empty);
        cache.Save(null, new ProfileNames("Unknown", "Unknown", "Unknown"));
        Check(Directory.GetFiles(folder, "*.json").Length == 1);

        var reopened = new ProfileNameCache(folder);
        Check(reopened.Read("AA-11-22-33-44-55") == saved);
        reopened.Save("AA1122334455", new ProfileNames("Updated", "Silent Hill", ""));
        Check(reopened.Read("AA1122334455").Circle == "Updated");
    }
    finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
});
Test("Unreadable cached names do not fabricate a profile", () =>
{
    string folder = Path.Combine(Path.GetTempPath(), "NSEdge-cache-tests-" + Guid.NewGuid().ToString("N"));
    try
    {
        var cache = new ProfileNameCache(folder);
        cache.Save("AA1122334455", new ProfileNames("DarkSouls", null, null));
        string filename = Directory.GetFiles(folder, "*.json").Single();
        File.WriteAllText(filename, "{ invalid json");
        Check(cache.Read("AA1122334455") == ProfileNames.Empty);

        File.WriteAllText(Path.Combine(folder, "identity.key"), "invalid");
        bool rejected = false;
        try { _ = new ProfileNameCache(folder); }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected);
    }
    finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
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
