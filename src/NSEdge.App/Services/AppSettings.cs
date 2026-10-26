using System.IO;
using System.Text.Json;
using NSEdge.Core;

namespace NSEdge.App.Services;

public sealed class AppSettings
{
    public bool ShowNotifications { get; set; } = true;
    public double NotificationSeconds { get; set; } = 2.5;
    public bool ShowBatteryOnPs { get; set; } = true;
    public bool ShowLowBatteryAlerts { get; set; } = false;
    public int LowBatteryThresholdPercent { get; set; } = 20;
    public bool NativeLightingEnabled { get; set; } = false;
    public LightbarMode LightingMode { get; set; } = LightbarMode.BatteryBands;
    public string LightbarCritical { get; set; } = "#EF4657";
    public string LightbarLow { get; set; } = "#F58436";
    public string LightbarMedium { get; set; } = "#F2BA42";
    public string LightbarHigh { get; set; } = "#26CE78";
    public string LightbarCharging { get; set; } = "#499DFF";
    public string LightbarFull { get; set; } = "#26CE78";
    public string LightbarStatic { get; set; } = "#75B8FF";
    public int LightbarCriticalBelow { get; set; } = 20;
    public int LightbarLowBelow { get; set; } = 40;
    public int LightbarMediumBelow { get; set; } = 70;

    public LightbarPalette? LightingPalette()
    {
        if (!LightbarColor.TryParse(LightbarCritical, out var critical) ||
            !LightbarColor.TryParse(LightbarLow, out var low) ||
            !LightbarColor.TryParse(LightbarMedium, out var medium) ||
            !LightbarColor.TryParse(LightbarHigh, out var high) ||
            !LightbarColor.TryParse(LightbarCharging, out var charging) ||
            !LightbarColor.TryParse(LightbarFull, out var full) ||
            !LightbarColor.TryParse(LightbarStatic, out var staticColor))
            return null;
        var palette = new LightbarPalette
        {
            Critical = critical, Low = low, Medium = medium, High = high,
            Charging = charging, Full = full, Static = staticColor,
            CriticalBelowPercent = LightbarCriticalBelow,
            LowBelowPercent = LightbarLowBelow,
            MediumBelowPercent = LightbarMediumBelow
        };
        return palette.HasValidThresholds ? palette : null;
    }
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string path;

    public SettingsStore(string? filePath = null) => path = Path.GetFullPath(filePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NSEdge", "settings.json"));

    public event Action<string>? SaveFailed;

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(path))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions);
                if (settings is not null)
                {
                    settings.NotificationSeconds = double.IsFinite(settings.NotificationSeconds)
                        ? Math.Clamp(settings.NotificationSeconds, 1, 10) : 2.5;
                    settings.LowBatteryThresholdPercent = Math.Clamp(
                        (int)Math.Round(Math.Clamp(settings.LowBatteryThresholdPercent, 10, 50) / 10.0) * 10, 10, 50);
                    if (!Enum.IsDefined(settings.LightingMode) || settings.LightingPalette() is null)
                        settings.NativeLightingEnabled = false;
                    return settings;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Write("Could not read settings: " + ex.Message);
        }
        return new AppSettings();
    }

    public bool Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write("Could not save settings: " + ex.Message);
            SaveFailed?.Invoke(ex.Message);
            return false;
        }
    }
}
