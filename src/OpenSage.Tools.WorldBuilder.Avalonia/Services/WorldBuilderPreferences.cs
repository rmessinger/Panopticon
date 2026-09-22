using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace OpenSage.Tools.WorldBuilder.Avalonia.Services;

/// <summary>
/// Persists editor settings (game install paths) as JSON under the OpenSAGE
/// app-data directory. Replaces the MAUI Preferences store.
/// </summary>
public static class WorldBuilderPreferences
{
    private const string KeyGenerals = "GamePath_Generals";
    private const string KeyZeroHour = "GamePath_ZeroHour";
    private const string KeyBfme = "GamePath_Bfme";

    private static string PreferencesPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OpenSAGE",
        "WorldBuilder",
        "settings.json");

    private static Dictionary<string, string> Load()
    {
        try
        {
            if (File.Exists(PreferencesPath))
            {
                using var stream = File.OpenRead(PreferencesPath);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
            }
        }
        catch (Exception)
        {
            // Corrupt settings fall back to defaults.
        }

        return [];
    }

    private static void Save(Dictionary<string, string> values)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath)!);
        File.WriteAllText(PreferencesPath, JsonSerializer.Serialize(values));
    }

    public static string Get(string key, string fallback = "")
    {
        var values = Load();
        return values.TryGetValue(key, out var value) ? value : fallback;
    }

    public static void Set(string key, string value)
    {
        var values = Load();
        values[key] = value;
        Save(values);
    }

    public static string GeneralsPath => Get(KeyGenerals, Environment.GetEnvironmentVariable("CNC_GENERALS_PATH") ?? "");

    public static string ZeroHourPath => Get(KeyZeroHour, Environment.GetEnvironmentVariable("CNC_GENERALS_ZH_PATH") ?? "");

    public static string BfmePath => Get(KeyBfme, Environment.GetEnvironmentVariable("BFME_PATH") ?? "");

    public static void SavePaths(string generals, string zeroHour, string bfme)
    {
        Set(KeyGenerals, generals);
        Set(KeyZeroHour, zeroHour);
        Set(KeyBfme, bfme);
    }
}
