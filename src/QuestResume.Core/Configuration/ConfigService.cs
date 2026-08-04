using System.Text.Json;

namespace QuestResume.Core.Configuration;

public sealed class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    
    private AppOptions? _cached;
    private DateTime _cachedStamp = DateTime.MinValue;
    private readonly object _cacheLock = new();

    public string ConfigPath { get; }

    public ConfigService(string? configPath = null)
    {
        ConfigPath = configPath ?? GetDefaultConfigPath();
    }

        public const string PortableMarkerFileName = "portable.marker";

        public static string GetBaseDataDirectory()
    {
        var exeDir = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(exeDir, PortableMarkerFileName)))
        {
            return Path.Combine(exeDir, "QuestResumeData");
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "QuestResume");
    }

        public static bool IsPortableMode() =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, PortableMarkerFileName));

    public static string GetDefaultConfigPath() => Path.Combine(GetBaseDataDirectory(), "config.json");

    public static string GetDefaultIndexPath() => Path.Combine(GetBaseDataDirectory(), "index");

    public static string GetDefaultLogsPath() => Path.Combine(GetBaseDataDirectory(), "logs");

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

        
        lock (_cacheLock) { _cached = null; }
    }

        private static readonly string[] SecretMarkers = { "password", "secret", "token", "apikey", "clientid", "credential" };

        public string ExportConfig()
    {
        var options = Load();
        var json = JsonSerializer.Serialize(options, JsonOptions);
        var node = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

        var redacted = new Dictionary<string, object?>
        {
            ["_aviso"] = "Segredos (senhas, tokens, apikeys, clientids) foram redigidos como \"***REDACTED***\" e precisam ser reconfigurados após importar."
        };

        foreach (var (key, value) in node)
        {
            var isSecret = SecretMarkers.Any(m => key.Contains(m, StringComparison.OrdinalIgnoreCase))
                           && value.ValueKind == JsonValueKind.String
                           && !string.IsNullOrEmpty(value.GetString());
            redacted[key] = isSecret ? "***REDACTED***" : (object?)value;
        }

        return JsonSerializer.Serialize(redacted, JsonOptions);
    }

        public AppOptions ImportConfig(string json)
    {
        var incoming = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
                       ?? throw new JsonException("JSON de configuração vazio ou inválido.");
        incoming.Remove("_aviso");

        var current = Load();
        var currentDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            JsonSerializer.Serialize(current, JsonOptions))!;

        
        foreach (var (key, value) in incoming)
        {
            if (value.ValueKind == JsonValueKind.String && value.GetString() == "***REDACTED***")
                continue;
            currentDict[key] = value;
        }

        var merged = JsonSerializer.Deserialize<AppOptions>(JsonSerializer.Serialize(currentDict))
                     ?? throw new JsonException("Não foi possível desserializar a configuração importada.");

        if (string.IsNullOrWhiteSpace(merged.IndexPath))
            merged.IndexPath = GetDefaultIndexPath();

        Save(merged); 
        return merged;
    }
}
