using NSEdge.App.Services;
using NSEdge.Core;

internal static class SettingsStoreTests
{
    public static int Run()
    {
        string folder = Path.Combine(Path.GetTempPath(), "NSEdge-settings-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        Log.InitializeForSmokeTest(folder);
        int passed = 0;
        try
        {
            string path = Path.Combine(folder, "settings.json");
            var store = new SettingsStore(path);
            Check(!store.Load().NativeLightingEnabled, "Missing settings default to lighting off");

            // Old versions omit new fields; initializers are the migration defaults.
            File.WriteAllText(path, "{\"ShowNotifications\":false,\"NotificationSeconds\":99,\"LowBatteryThresholdPercent\":2147483647}");
            var old = store.Load();
            Check(!old.ShowNotifications && old.NotificationSeconds == 10 &&
                old.LowBatteryThresholdPercent == 50 && !old.NativeLightingEnabled,
                "Older settings retain preferences and clamp extreme values without overflow");

            old.LightbarStatic = "#123456";
            old.LightingMode = LightbarMode.Static;
            old.NativeLightingEnabled = true;
            Check(store.Save(old) && store.Load().LightbarStatic == "#123456" &&
                store.Load().NativeLightingEnabled && !File.Exists(path + ".tmp"),
                "Settings round-trip through replacement without leftover temporary data");

            File.WriteAllText(path, "{\"NativeLightingEnabled\":true,\"LightingMode\":999}");
            Check(!store.Load().NativeLightingEnabled, "Unknown lighting mode fails closed");
            File.WriteAllText(path, "{\"NativeLightingEnabled\":true,\"LightbarHigh\":null}");
            Check(!store.Load().NativeLightingEnabled, "Invalid palette fails closed");
            File.WriteAllText(path, "{broken");
            Check(!store.Load().NativeLightingEnabled && File.ReadAllText(path) == "{broken",
                "Malformed settings fall back without overwriting original evidence");

            // A directory at the destination deterministically fails without relying on ACLs.
            string blockedPath = Path.Combine(folder, "blocked.json");
            Directory.CreateDirectory(blockedPath);
            var blocked = new SettingsStore(blockedPath);
            int failureCount = 0;
            blocked.SaveFailed += message => { if (!string.IsNullOrWhiteSpace(message)) failureCount++; };
            Check(!blocked.Save(new AppSettings()) && failureCount == 1 && Directory.Exists(blockedPath),
                "Save failure is returned and reported, preserving existing destination");

            return passed;
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }

        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            passed++;
            Console.WriteLine("PASS " + name);
        }
    }
}