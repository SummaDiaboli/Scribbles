using System.Text.Json;

namespace Scribbles.Core;

/// <summary>Loads/saves <see cref="AppSettings"/> as JSON. Missing or corrupt
/// files fall back to defaults (never throws).</summary>
public static class SettingsService
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Scribbles", "settings.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path))
                return AppSettings.Default;
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options);
            if (settings is null)
                return AppSettings.Default;
            // Files written by older builds lack newer fields (null arrays
            // crashed startup in 2026-10-08): backfill from defaults.
            return settings with
            {
                Palette = settings.Palette ?? AppSettings.Default.Palette,
                RecentFiles = settings.RecentFiles ?? AppSettings.Default.RecentFiles,
                LastPages = settings.LastPages ?? AppSettings.Default.LastPages,
                UpdateChannel = UpdateChannels.Normalize(settings.UpdateChannel),
            };
        }
        catch
        {
            return AppSettings.Default;
        }
    }

    public static void Save(string? path, AppSettings settings)
    {
        path ??= DefaultPath;
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
    }
}
