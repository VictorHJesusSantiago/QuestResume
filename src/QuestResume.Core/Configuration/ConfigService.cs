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

    // Cache: avoid re-reading the JSON file on every request when the file hasn't changed.
    private AppOptions? _cached;
    private DateTime _cachedStamp = DateTime.MinValue;
    private readonly object _cacheLock = new();

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

    /// <summary>
    /// Loads <see cref="AppOptions"/> from <see cref="ConfigPath"/>. If the file is missing,
    /// empty, or contains invalid JSON (e.g. truncated by a crash mid-write), falls back to
    /// default options instead of throwing — the shared config file is read by three
    /// independent front-ends, so a transient corruption in one must not crash the others.
    /// </summary>
    public AppOptions Load()
    {
        var stamp = File.Exists(ConfigPath)
            ? File.GetLastWriteTimeUtc(ConfigPath)
            : DateTime.MinValue;

        lock (_cacheLock)
        {
            if (_cached is not null && stamp == _cachedStamp)
                return _cached;
        }

        var options = LoadFromDisk();

        lock (_cacheLock)
        {
            _cached = options;
            _cachedStamp = stamp;
        }

        return options;
    }

    private AppOptions LoadFromDisk()
    {
        if (!File.Exists(ConfigPath))
            return new AppOptions { IndexPath = GetDefaultIndexPath() };

        AppOptions? options;
        try
        {
            var json = File.ReadAllText(ConfigPath);
            options = JsonSerializer.Deserialize<AppOptions>(json);
        }
        catch (JsonException)
        {
            return new AppOptions { IndexPath = GetDefaultIndexPath() };
        }

        if (options is null)
            return new AppOptions { IndexPath = GetDefaultIndexPath() };

        if (string.IsNullOrWhiteSpace(options.IndexPath))
            options.IndexPath = GetDefaultIndexPath();

        return options;
    }

    /// <summary>
    /// Validates and persists <paramref name="options"/> to <see cref="ConfigPath"/>.
    /// The write is performed via a temp file + atomic <see cref="File.Move"/> so a crash
    /// mid-write can never leave the shared config file truncated/corrupted.
    /// </summary>
    /// <exception cref="AppOptionsValidationException">When <paramref name="options"/> has an invalid combination of values.</exception>
    public void Save(AppOptions options)
    {
        options.Validate();

        var directory = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(options, JsonOptions);
        var tempPath = ConfigPath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, ConfigPath, overwrite: true);

        // Invalidate cache so the next Load() re-reads the file we just wrote.
        lock (_cacheLock) { _cached = null; }
    }
}
