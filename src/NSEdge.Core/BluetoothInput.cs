using System.Buffers.Binary;

namespace NSEdge.Core;

public enum BluetoothReportKind
{
    Invalid,
    Minimal,
    Extended
}

public readonly record struct BluetoothInput(
    EdgeBattery Battery, bool PsPressed, byte RawBatteryStatus, byte EdgeProfileCandidate);

public static class BluetoothInputDecoder
{
    private static readonly uint[] CrcTable = CreateTable();

    public static BluetoothReportKind Decode(ReadOnlySpan<byte> report, out BluetoothInput input)
    {
        input = default;

        // Bluetooth may initially send minimal report 0x01 until another host enables extended mode.
        if (report.Length > 0 && report[0] == 0x01)
            return BluetoothReportKind.Minimal;

        if (report.Length != 78 || report[0] != 0x31 || !HasValidCrc(report))
            return BluetoothReportKind.Invalid;

        // Linux hid-playstation.c locates its shared DualSense payload at byte 2 on BT,
        // one byte after the USB payload. Edge-specific profile metadata remains unverified.
        byte raw = report[54];
        EdgeInputDecoder.DecodeBatteryStatus(raw, out EdgeBattery battery);
        input = new BluetoothInput(battery, (report[11] & 0x01) != 0, raw, report[50]);
        return BluetoothReportKind.Extended;
    }

    public static bool HasValidCrc(ReadOnlySpan<byte> report)
    {
        if (report.Length != 78 || report[0] != 0x31)
            return false;

        uint expected = BinaryPrimitives.ReadUInt32LittleEndian(report[74..78]);
        return expected == CalculateInputCrc(report[..74]);
    }

    public static uint CalculateInputCrc(ReadOnlySpan<byte> reportWithoutCrc)
    {
        uint crc = 0xFFFFFFFFu;
        crc = (crc >> 8) ^ CrcTable[(int)((crc ^ 0xA1) & 0xFF)];
        foreach (byte value in reportWithoutCrc)
            crc = (crc >> 8) ^ CrcTable[(int)((crc ^ value) & 0xFF)];
        return ~crc;
    }

    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint value = i;
            for (int b = 0; b < 8; b++)
                value = (value >> 1) ^ ((value & 1) != 0 ? 0xEDB88320u : 0u);
            table[i] = value;
        }
        return table;
    }
}

public sealed class StableByteObserver(int requiredReports = 12)
{
    private byte? candidate;
    private int consecutive;

    public byte? Current { get; private set; }

    public bool Observe(byte value)
    {
        if (candidate != value)
        {
            candidate = value;
            consecutive = 1;
        }
        else if (consecutive < requiredReports)
        {
            consecutive++;
        }

        if (consecutive < requiredReports || Current == value)
            return false;

        Current = value;
        return true;
    }
}
