using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using NSEdge.Core;

namespace NSEdge.Windows.Hid;

internal static partial class NativeMethods
{
    private const uint DigcfPresent = 0x02;
    private const uint DigcfDeviceInterface = 0x10;
    private static readonly Guid HidGuid = new("4d1e55b2-f16f-11cf-88cb-001111000030");

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInterfaceData
    {
        public int Size;
        public Guid InterfaceClassGuid;
        public int Flags;
        public nint Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SetupDiGetClassDevs(ref Guid classGuid, nint enumerator, nint parent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(nint list, nint data, ref Guid guid, uint index, ref DeviceInterfaceData item);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(nint list, ref DeviceInterfaceData item, nint detail, uint size, out uint required, nint data);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(nint list);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetFeature(SafeFileHandle handle, [In, Out] byte[] report, int length);

    public static byte[]? GetFeature(SafeFileHandle handle, byte reportId)
    {
        foreach (int length in new[] { 64, 65 })
        {
            byte[] buffer = new byte[length];
            buffer[0] = reportId;
            if (HidD_GetFeature(handle, buffer, length) && buffer[0] == reportId)
                return buffer;
        }
        return null;
    }

    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetSerialNumberString(SafeFileHandle handle,
        StringBuilder buffer, int bufferLength);

    public static string? TryReadBluetoothIdentity(SafeFileHandle handle)
    {
        var value = new StringBuilder(128);
        return HidD_GetSerialNumberString(handle, value, 256)
            ? ControllerIdentity.Normalize(value.ToString())
            : null;
    }

    public static (string? Identity, string Status) ReadUsbIdentity(SafeFileHandle handle)
    {
        const byte pairingReport = 0x09;
        int length;
        try { length = ReadCaps(handle).FeatureReportByteLength; }
        catch (Exception ex) when (ex is IOException or Win32Exception)
        {
            return (null, "Cannot read USB HID feature capabilities");
        }

        if (length < 20)
            return (null, "USB pairing feature buffer is too small");

        byte[] report = new byte[length];
        report[0] = pairingReport;
        if (!HidD_GetFeature(handle, report, report.Length))
        {
            int error = Marshal.GetLastWin32Error();
            return (null, $"USB pairing report 0x09 unavailable (Windows error {error})");
        }
        if (report[0] != pairingReport)
            return (null, "USB pairing report ID was unexpected");

        string? identity = ControllerIdentity.FromUsbAddress(report);
        return identity is null
            ? (null, "USB pairing report contains no usable controller identity")
            : (identity, "USB pairing report 0x09 received");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidCaps
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
        public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out nint data);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_FreePreparsedData(nint data);

    [DllImport("hid.dll", ExactSpelling = true)]
    private static extern int HidP_GetCaps(nint preparsedData, out HidCaps capabilities);

    [StructLayout(LayoutKind.Sequential)]
    private struct HidAttributes
    {
        public int Size;
        public ushort VendorId, ProductId, VersionNumber;
    }

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetAttributes(SafeFileHandle handle, ref HidAttributes attributes);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle OpenMetadata(string path, uint access, FileShare share,
        nint security, FileMode disposition, uint flags, nint template);

    public static HidScanResult ScanEdgeDevices()
    {
        var devices = new List<HidDevice>();
        int scanned = 0, matching = 0, blocked = 0, unsupported = 0;
        Guid guid = HidGuid;
        nint list = SetupDiGetClassDevs(ref guid, 0, 0, DigcfPresent | DigcfDeviceInterface);
        if (list == 0 || list == -1)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "HID enumeration failed");
        try
        {
            for (uint index = 0; index < 256; index++)
            {
                var item = new DeviceInterfaceData { Size = Marshal.SizeOf<DeviceInterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(list, 0, ref guid, index, ref item))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error != 259)
                        throw new Win32Exception(error, "HID enumeration failed");
                    break;
                }
                scanned++;
                SetupDiGetDeviceInterfaceDetail(list, ref item, 0, 0, out uint needed, 0);
                if (needed is < 8 or > 32768) continue;
                nint detail = Marshal.AllocHGlobal((int)needed);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(list, ref item, detail, needed, out _, 0))
                    {
                        blocked++;
                        continue;
                    }

                    string? path = Marshal.PtrToStringUni(detail + 4);
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    using SafeFileHandle handle = OpenMetadata(path, 0, FileShare.ReadWrite,
                        0, FileMode.Open, 0, 0);
                    if (handle.IsInvalid) { blocked++; continue; }

                    var attr = new HidAttributes { Size = Marshal.SizeOf<HidAttributes>() };
                    if (!HidD_GetAttributes(handle, ref attr)) { blocked++; continue; }
                    if (attr.VendorId != EdgeProfileDecoder.VendorId ||
                        attr.ProductId != EdgeProfileDecoder.ProductId) continue;
                    matching++;

                    try
                    {
                        HidCaps caps = ReadCaps(handle);
                        var info = new EdgeHidDescriptor(attr.VendorId, attr.ProductId,
                            caps.UsagePage, caps.Usage, caps.InputReportByteLength);
                        EdgeTransport? transport = EdgeHidIdentity.Match(info);
                        if (transport is EdgeTransport t)
                            devices.Add(new HidDevice(path, t, caps.InputReportByteLength,
                                caps.UsagePage, caps.Usage));
                        else unsupported++;
                    }
                    catch (IOException) { unsupported++; }
                    catch (Win32Exception) { unsupported++; }
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(list); }

        return new HidScanResult(devices.OrderBy(d => d.Transport == EdgeTransport.Usb ? 0 : 1)
            .ToArray(), scanned, matching, blocked, unsupported);
    }

    private static HidCaps ReadCaps(SafeFileHandle handle)
    {
        if (!HidD_GetPreparsedData(handle, out nint data))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot read HID capabilities");
        try
        {
            int status = HidP_GetCaps(data, out HidCaps caps);
            if (status < 0 || caps.InputReportByteLength == 0)
                throw new IOException($"Invalid HID capabilities: 0x{status:X8}");
            return caps;
        }
        finally { HidD_FreePreparsedData(data); }
    }

    public static int GetInputReportLength(SafeFileHandle handle) =>
        ReadCaps(handle).InputReportByteLength;
}

internal sealed record HidDevice(string Path, EdgeTransport Transport,
    int InputReportLength, ushort UsagePage, ushort Usage);

internal sealed record HidScanResult(IReadOnlyList<HidDevice> Devices,
    int InterfacesScanned, int EdgeInterfaces, int MetadataFailures, int UnsupportedInterfaces)
{
    public string Summary => $"HID: {InterfacesScanned} interfaces; " +
        $"Edge: {EdgeInterfaces}; supported: {Devices.Count}; " +
        $"metadata failures: {MetadataFailures}; unsupported: {UnsupportedInterfaces}";
}
