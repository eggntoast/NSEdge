namespace NSEdge.Core;

public static class Ds4ControllerTarget
{
    public static bool IsMatch(string? expectedController, string? reportedController, string? transport)
    {
        string? expected = ControllerIdentity.Normalize(expectedController);
        return expected is not null &&
            expected == ControllerIdentity.Normalize(reportedController) &&
            (string.Equals(transport?.Trim(), "BT", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(transport?.Trim(), "Bluetooth", StringComparison.OrdinalIgnoreCase));
    }
}
