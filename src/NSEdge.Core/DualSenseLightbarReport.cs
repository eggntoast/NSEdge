using System.Buffers.Binary;

namespace NSEdge.Core;

public static class DualSenseLightbarReport
{
    // hid-playstation.c: output report common payload begins at 1 on USB and 3 on Bluetooth.
    // Setting only valid_flag1 bit 2 avoids commanding the motors, triggers or other LEDs.
    public static byte[] BuildUsb(LightbarColor color, int reportLength = 63)
    {
        if (reportLength is not (63 or 64))
            throw new ArgumentOutOfRangeException(nameof(reportLength));

        byte[] report = new byte[reportLength];
        report[0] = 0x02;
        PutColor(report, 1, color);
        return report;
    }

    public static byte[] BuildBluetooth(LightbarColor color, byte sequence)
    {
        byte[] report = new byte[78];
        report[0] = 0x31;
        report[1] = (byte)((sequence & 0x0F) << 4);
        report[2] = 0x10;
        PutColor(report, 3, color);
        BinaryPrimitives.WriteUInt32LittleEndian(report.AsSpan(74, 4), CalculateOutputCrc(report.AsSpan(0, 74)));
        return report;
    }

    public static byte[] BuildUsbSetup(int reportLength = 63)
    {
        if (reportLength is not (63 or 64))
            throw new ArgumentOutOfRangeException(nameof(reportLength));
        byte[] report = new byte[reportLength];
        report[0] = 0x02;
        PutSetup(report, 1);
        return report;
    }

    public static byte[] BuildBluetoothSetup(byte sequence)
    {
        byte[] report = new byte[78];
        report[0] = 0x31;
        report[1] = (byte)((sequence & 0x0F) << 4);
        report[2] = 0x10;
        PutSetup(report, 3);
        BinaryPrimitives.WriteUInt32LittleEndian(report.AsSpan(74, 4), CalculateOutputCrc(report.AsSpan(0, 74)));
        return report;
    }

    private static void PutSetup(byte[] report, int start)
    {
        report[start + 38] = 0x02; // Enable LED setup only.
        // Linux hid-playstation.c DS_OUTPUT_LIGHTBAR_SETUP_LIGHT_OUT is BIT(1).
        report[start + 41] = 0x02; // Fade startup animation out before RGB commands.
    }

    private static void PutColor(byte[] report, int start, LightbarColor color)
    {
        report[start + 1] = 0x04;
        report[start + 44] = color.Red;
        report[start + 45] = color.Green;
        report[start + 46] = color.Blue;
    }

    public static uint CalculateOutputCrc(ReadOnlySpan<byte> payload)
    {
        uint crc = 0xFFFFFFFF;
        static uint Update(uint crc, byte value)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) == 1 ? 0xEDB88320u : 0u);
            return crc;
        }

        crc = Update(crc, 0xA2);
        foreach (byte value in payload)
            crc = Update(crc, value);
        return ~crc;
    }
}
