namespace NSEdge.Core;

public enum EdgeTransport
{
    Usb,
    Bluetooth
}

public static class EdgeTransportDetector
{
    public static EdgeTransport? FromInputReportLength(int length) => length switch
    {
        64 => EdgeTransport.Usb,
        78 => EdgeTransport.Bluetooth,
        _ => null
    };
}
