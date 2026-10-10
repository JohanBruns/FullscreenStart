using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using FullscreenStart.Models;

namespace FullscreenStart.Services;

/// <summary>Reads and writes layout.json and settings.json in %AppData%\FullscreenStart.</summary>
public sealed class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // Keep umlauts and '+' readable in the files.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public JsonStore(string? directory = null)
    {
        Directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FullscreenStart");
        System.IO.Directory.CreateDirectory(Directory);
    }

    public string Directory { get; }
    public string LayoutPath => Path.Combine(Directory, "layout.json");
    public string SettingsPath => Path.Combine(Directory, "settings.json");

    /// <summary>Returns the saved layout, or null if none exists yet (first run).</summary>
    public StartLayout? LoadLayout() => Load<StartLayout>(LayoutPath);

    public void SaveLayout(StartLayout layout) => Save(LayoutPath, layout);

    public AppSettings LoadSettings()
    {
        var settings = Load<AppSettings>(SettingsPath);
        if (settings == null)
        {
            settings = new AppSettings();
            SaveSettings(settings);
        }
        return settings;
    }

    public void SaveSettings(AppSettings settings) => Save(SettingsPath, settings);

    private static T? Load<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(stream, Options);
        }
        catch (JsonException)
        {
            // Keep the broken file for inspection instead of silently overwriting it.
            File.Copy(path, path + ".broken", overwrite: true);
            return null;
        }
    }

    private static void Save<T>(string path, T value)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        File.Move(temp, path, overwrite: true);
    }
}
