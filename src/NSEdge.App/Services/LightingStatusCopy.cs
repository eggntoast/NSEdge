namespace NSEdge.App.Services;

/// <summary>User-facing status text. Never equate a sent HID command with a physical LED confirmation.</summary>
public static class LightingStatusCopy
{
    public static string Describe(bool enabled, string? diagnostic)
    {
        if (!enabled) return "Controller lighting is off";
        string value = diagnostic ?? string.Empty;
        if (value.Contains("Other controller software", StringComparison.OrdinalIgnoreCase))
            return "Paused — another controller app is controlling the lights";
        if (value.Contains("command sent", StringComparison.OrdinalIgnoreCase))
            return "Colour sent — check the controller";
        if (value.Contains("Waiting for a controller", StringComparison.OrdinalIgnoreCase))
            return "Waiting for controller input";
        if (value.Contains("Battery status unavailable", StringComparison.OrdinalIgnoreCase))
            return "Waiting for battery information";
        if (value.Contains("Lighting unavailable", StringComparison.OrdinalIgnoreCase))
            return "Could not update the controller lights — see Diagnostics";
        if (value.Contains("Invalid lighting settings", StringComparison.OrdinalIgnoreCase))
            return "Check your lighting settings";
        if (value.Contains("Lighting is off", StringComparison.OrdinalIgnoreCase))
            return "Preparing controller lighting";
        return "Checking controller lighting";
    }
}
