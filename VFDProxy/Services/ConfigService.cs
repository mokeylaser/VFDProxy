using System.Diagnostics;
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

    /// <summary>
    /// Last error from Load or Save, if any. Null when the last operation succeeded.
    /// </summary>
    public static string? LastError { get; private set; }

    public static AppConfig Load()
    {
        LastError = null;
        try
        {
            if (!File.Exists(ConfigFile)) return new AppConfig();
            var json = File.ReadAllText(ConfigFile);
            return JsonSerializer.Deserialize<AppConfig>(json, JsonOpts) ?? new AppConfig();
        }
        catch (Exception ex)
        {
            LastError = $"Failed to load config: {ex.Message}";
            Trace.TraceWarning(LastError);
            return new AppConfig();
        }
    }

    public static void Save(AppConfig config)
    {
        LastError = null;
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(config, JsonOpts);
            File.WriteAllText(TempFile, json);
            File.Move(TempFile, ConfigFile, overwrite: true);
        }
        catch (Exception ex)
        {
            LastError = $"Failed to save config: {ex.Message}";
            Trace.TraceWarning(LastError);
        }
    }
}
