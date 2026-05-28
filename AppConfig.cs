using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Romestead.MapWorkshop;

/// <summary>
/// Persisted user preferences. Stored in %LOCALAPPDATA%\Romestead.MapWorkshop\config.json
/// so we don't need to write next to the exe (which may be in Program Files / a
/// read-only location for normal users).
/// </summary>
internal sealed class AppConfig
{
    public string? GameRoot { get; set; }

    [JsonIgnore]
    public static string ConfigDir { get; } = ResolveConfigDir();

    [JsonIgnore]
    public static string ConfigPath => Path.Combine(ConfigDir, "config.json");

    private static string ResolveConfigDir()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(local, "Romestead.MapWorkshop");
        try { Directory.CreateDirectory(dir); } catch { dir = Path.GetTempPath(); }
        return dir;
    }

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                if (cfg != null) return cfg;
            }
        }
        catch { /* fall through to a fresh config */ }
        return new AppConfig();
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch { /* best effort - config is convenience, not correctness */ }
    }
}
