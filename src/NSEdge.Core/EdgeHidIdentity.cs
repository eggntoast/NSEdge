namespace NSEdge.Core;

public readonly record struct EdgeHidDescriptor(
    ushort VendorId, ushort ProductId, ushort UsagePage, ushort Usage, int InputReportLength);

public static class EdgeHidIdentity
{
    public static EdgeTransport? Match(EdgeHidDescriptor d)
    {
        if (d.VendorId != EdgeProfileDecoder.VendorId ||
            d.ProductId != EdgeProfileDecoder.ProductId ||
            d.UsagePage != 0x01 ||
            d.Usage is not (0x04 or 0x05))
            return null;

        return EdgeTransportDetector.FromInputReportLength(d.InputReportLength);
    }
}
