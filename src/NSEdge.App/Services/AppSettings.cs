using System.IO;
using System.Text.Json;

namespace NSEdge.App.Services;

public sealed class AppSettings
{
    public bool ShowNotifications { get; set; } = true;
    public double NotificationSeconds { get; set; } = 2.5;
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NSEdge", "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(path))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions);
                if (settings is not null)
                {
                    settings.NotificationSeconds = Math.Clamp(settings.NotificationSeconds, 1, 10);
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

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write("Could not save settings: " + ex.Message);
        }
    }
}
