using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace NSEdge.App.Services;

internal static class WindowAppearance
{
    private const int ImmersiveDarkMode = 20;
    private const int BorderColor = 34;
    private const int CaptionColor = 35;
    private const int CaptionTextColor = 36;

    private const int Base = 0x20140E;
    private const int Text = 0xFCF6F2;
    private const int Border = 0x5B4635;

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    public static void Apply(nint hwnd)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || hwnd == 0)
            return;

        if (SystemParameters.HighContrast)
            return;

        Set(hwnd, ImmersiveDarkMode, 1);
        Set(hwnd, CaptionColor, Base);
        Set(hwnd, CaptionTextColor, Text);
        Set(hwnd, BorderColor, Border);
    }

    private static void Set(nint hwnd, int attribute, int value)
    {
        int result = DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));
        if (result != 0)
            Log.Write($"DWM attribute {attribute} unavailable: HRESULT 0x{result:X8}");
    }
}