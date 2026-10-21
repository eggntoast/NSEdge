using Microsoft.Win32;

namespace NSEdge.App.Services;

public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "NSEdge";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return string.Equals(key?.GetValue(Name) as string, Command(), StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
            key.SetValue(Name, Command(), RegistryValueKind.String);
        else
            key.DeleteValue(Name, throwOnMissingValue: false);
    }

    private static string Command() => '"' + Environment.ProcessPath + '"';
}
