using System.Text.Json;

namespace MotionMaker.Core;

public sealed class UserSettings
{
    public bool InvertWheel { get; set; }

    public double OrbitSensitivity { get; set; } = 1;

    public double PanSensitivity { get; set; } = 1;

    public double SnapSpacingMm { get; set; } = 1;

    public bool SnapEnabled { get; set; } = true;

    public bool ShowGrid { get; set; } = true;

    public double WindowWidth { get; set; } = 1280;

    public double WindowHeight { get; set; } = 800;

    public List<string> RecentFiles { get; set; } = [];

    public void RememberFile(string path)
    {
        RecentFiles.RemoveAll(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > 8)
        {
            RecentFiles.RemoveRange(8, RecentFiles.Count - 8);
        }
    }
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MotionMaker",
        "settings.json");

    public static UserSettings Load() => Load(DefaultPath);

    public static UserSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            return new UserSettings();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path), Options) ?? new UserSettings();
            settings.RecentFiles ??= [];
            settings.OrbitSensitivity = settings.OrbitSensitivity <= 0 ? 1 : settings.OrbitSensitivity;
            settings.PanSensitivity = settings.PanSensitivity <= 0 ? 1 : settings.PanSensitivity;
            settings.SnapSpacingMm = settings.SnapSpacingMm <= 0 ? 1 : settings.SnapSpacingMm;
            if (settings.WindowWidth < 640)
            {
                settings.WindowWidth = 1280;
            }

            if (settings.WindowHeight < 480)
            {
                settings.WindowHeight = 800;
            }

            return settings;
        }
        catch (JsonException)
        {
            return new UserSettings();
        }
    }

    public static void Save(UserSettings settings) => Save(settings, DefaultPath);

    public static void Save(UserSettings settings, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
    }
}
