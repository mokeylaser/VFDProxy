using System.IO;
using System.Text.Json;
using VFDProxy.Models;

namespace VFDProxy.Services;

public static class ConfigService
{
    private static readonly string ConfigDir  = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VFDProxy");
    private static readonly string ConfigFile = Path.Combine(ConfigDir, "config.json");
    private static readonly string TempFile   = ConfigFile + ".tmp";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static AppConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigFile)) return new AppConfig();
            var json = File.ReadAllText(ConfigFile);
            return JsonSerializer.Deserialize<AppConfig>(json, JsonOpts) ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    public static void Save(AppConfig config)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(config, JsonOpts);
            File.WriteAllText(TempFile, json);
            File.Move(TempFile, ConfigFile, overwrite: true);
        }
        catch
        {
            // Best-effort; don't crash the app on save failure
        }
    }
}
