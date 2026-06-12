using System.Text.Json;

namespace QuestResume.Core.Configuration;

/// <summary>
/// Loads and saves <see cref="AppOptions"/> from a single JSON file shared by the CLI,
/// API and Desktop front-ends, so configuring the documents folder or model path in one
/// interface is immediately picked up by the others.
/// </summary>
public sealed class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string ConfigPath { get; }

    public ConfigService(string? configPath = null)
    {
        ConfigPath = configPath ?? GetDefaultConfigPath();
    }

    public static string GetDefaultConfigPath()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, "QuestResume", "config.json");
    }

    public static string GetDefaultIndexPath()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, "QuestResume", "index");
    }

    public AppOptions Load()
    {
        if (!File.Exists(ConfigPath))
        {
            return new AppOptions { IndexPath = GetDefaultIndexPath() };
        }

        var json = File.ReadAllText(ConfigPath);
        var options = JsonSerializer.Deserialize<AppOptions>(json);
        if (options is null)
        {
            return new AppOptions { IndexPath = GetDefaultIndexPath() };
        }

        if (string.IsNullOrWhiteSpace(options.IndexPath))
        {
            options.IndexPath = GetDefaultIndexPath();
        }

        return options;
    }

    public void Save(AppOptions options)
    {
        var directory = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(options, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }
}
