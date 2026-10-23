using System.ComponentModel;
using Microsoft.Win32.SafeHandles;
using NSEdge.Core;

namespace NSEdge.Windows.Hid;

public static class NativeLightbar
{
    public static void WriteColor(string devicePath, EdgeTransport transport, LightbarColor color,
        byte sequence, bool initialize)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
            throw new ArgumentException("Controller device path is unavailable.", nameof(devicePath));

        int outputLength = NativeMethods.GetOutputReportLength(devicePath, transport);
        byte[] report = transport switch
        {
            EdgeTransport.Usb => DualSenseLightbarReport.BuildUsb(color, outputLength),
            EdgeTransport.Bluetooth when outputLength == 78 => DualSenseLightbarReport.BuildBluetooth(color, sequence),
            _ => throw new IOException($"Unsupported output-report size: {outputLength}")
        };

        byte[]? setup = !initialize ? null : transport switch
        {
            EdgeTransport.Usb => DualSenseLightbarReport.BuildUsbSetup(outputLength),
            EdgeTransport.Bluetooth => DualSenseLightbarReport.BuildBluetoothSetup(sequence),
            _ => null
        };
        if (initialize && transport == EdgeTransport.Bluetooth)
            report = DualSenseLightbarReport.BuildBluetooth(color, (byte)(sequence + 1));

        using SafeFileHandle handle = File.OpenHandle(
            devicePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, FileOptions.None);
        using var stream = new FileStream(handle, FileAccess.Write, 128, isAsync: false);
        if (setup is not null)
            stream.Write(setup);
        stream.Write(report);
        stream.Flush();
    }
}
