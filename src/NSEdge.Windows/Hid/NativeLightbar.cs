using System.ComponentModel;
using Microsoft.Win32.SafeHandles;
using NSEdge.Core;

namespace NSEdge.Windows.Hid;

public static class NativeLightbar
{
    public static void WriteColor(string devicePath, EdgeTransport transport, LightbarColor color, byte sequence)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
            throw new ArgumentException("Controller device path is unavailable.", nameof(devicePath));

        int outputLength = NativeMethods.GetOutputReportLength(devicePath);
        byte[] report = transport switch
        {
            EdgeTransport.Usb => DualSenseLightbarReport.BuildUsb(color, outputLength),
            EdgeTransport.Bluetooth when outputLength == 78 => DualSenseLightbarReport.BuildBluetooth(color, sequence),
            _ => throw new IOException($"Unsupported output-report size: {outputLength}")
        };

        using SafeFileHandle handle = File.OpenHandle(
            devicePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, FileOptions.None);
        using var stream = new FileStream(handle, FileAccess.Write, 128, isAsync: false);
        stream.Write(report);
        stream.Flush();
    }
}
