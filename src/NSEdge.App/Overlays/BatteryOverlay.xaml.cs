using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using NSEdge.Core;

namespace NSEdge.App.Overlays;

public partial class BatteryOverlay : Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private readonly DispatcherTimer timer = new();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint newStyle);

    public BatteryOverlay()
    {
        InitializeComponent();
        timer.Tick += (_, _) => { timer.Stop(); Hide(); };
        SourceInitialized += (_, _) =>
        {
            nint hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLongPtr(hwnd, GwlExStyle, GetWindowLongPtr(hwnd, GwlExStyle) | (nint)WsExNoActivate);
        };
    }

    public void Announce(EdgeBattery battery, double seconds, bool lowWarning = false)
    {
        Heading.Text = lowWarning ? "DUALSENSE EDGE  ·  LOW BATTERY" : "DUALSENSE EDGE  ·  BATTERY";
        Reading.Text = battery.Description;
        Rect area = SystemParameters.WorkArea;
        Left = area.Right - Width - 24;
        Top = area.Top + 24;
        if (!IsVisible)
            Show();
        timer.Stop();
        timer.Interval = TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 10));
        timer.Start();
    }

    public void Stop()
    {
        timer.Stop();
        Hide();
    }
}