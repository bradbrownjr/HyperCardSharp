using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HyperCardSharp.App;

/// <summary>
/// Persists user-configurable application preferences to
/// %AppData%/HyperCardSharp/settings.json.
/// </summary>
public class AppSettings
{
    /// <summary>Path to the folder where the user has placed original Mac system fonts.</summary>
    [JsonPropertyName("userFontDirectory")]
    public string? UserFontDirectory { get; set; }

    /// <summary>Path to a HFS disk image that may contain system fonts.</summary>
    [JsonPropertyName("systemDiskImagePath")]
    public string? SystemDiskImagePath { get; set; }

    /// <summary>When true, render stacks in color mode instead of 1-bit black &amp; white.</summary>
    [JsonPropertyName("useColorMode")]
    public bool UseColorMode { get; set; }

    // ── Persistence helpers ───────────────────────────────────────────────────

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HyperCardSharp",
            "settings.json");

    public static AppSettings Load()
    {
        try
        {
            string path = SettingsPath;
            if (!File.Exists(path)) return new AppSettings();
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            string path = SettingsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string json = JsonSerializer.Serialize(this, _jsonOptions);
            File.WriteAllText(path, json);
        }
        catch
        {
            // Non-fatal — ignore write failures.
        }
    }
}
