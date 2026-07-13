using System;
using System.IO;
using System.Text.Json;

namespace WindowsHUD.Services;

public sealed class HudConfig
{
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public bool Locked { get; set; } = true;
    public bool AutoStart { get; set; } = false;
}

public static class ConfigService
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WindowsHUD");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    public static HudConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                string json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<HudConfig>(json);
                if (config != null)
                {
                    return config;
                }
            }
        }
        catch
        {
            // Fall through to defaults on any read/parse failure.
        }

        return new HudConfig();
    }

    public static void Save(HudConfig config)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch
        {
            // Best-effort persistence; ignore failures (e.g. read-only profile).
        }
    }
}
