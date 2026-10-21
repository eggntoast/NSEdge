using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

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

    public static IReadOnlyList<string> FindEdgePaths()
    {
        var result = new List<string>();
        Guid guid = HidGuid;
        nint list = SetupDiGetClassDevs(ref guid, 0, 0, DigcfPresent | DigcfDeviceInterface);
        if (list == nint.Zero || list == new nint(-1))
            return result;

        try
        {
            for (uint index = 0; index < 256; index++)
            {
                var item = new DeviceInterfaceData { Size = Marshal.SizeOf<DeviceInterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(list, 0, ref guid, index, ref item))
                    break;

                SetupDiGetDeviceInterfaceDetail(list, ref item, 0, 0, out uint required, 0);
                if (required is < 8 or > 32768)
                    continue;

                nint detail = Marshal.AllocHGlobal(checked((int)required));
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(list, ref item, detail, required, out _, 0))
                        continue;

                    string? path = Marshal.PtrToStringUni(detail + 4);
                    if (path is not null &&
                        path.Contains("vid_054c", StringComparison.OrdinalIgnoreCase) &&
                        path.Contains("pid_0df2", StringComparison.OrdinalIgnoreCase))
                        result.Add(path);
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(list);
        }
        return result;
    }
}
