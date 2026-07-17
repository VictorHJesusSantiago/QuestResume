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

    /// <summary>Nomes (case-insensitive) de propriedades cujo valor é redigido na exportação (item 17).</summary>
    private static readonly string[] SecretMarkers = { "password", "secret", "token", "apikey", "clientid", "credential" };

    /// <summary>
    /// Exporta o <see cref="AppOptions"/> atual como JSON, redigindo (substituindo por
    /// <c>"***REDACTED***"</c>) quaisquer propriedades cujo nome sugira um segredo
    /// (senha, secret, token, apikey, clientid, credential). Um campo <c>"_aviso"</c> documenta a
    /// redação no próprio arquivo exportado. Use para compartilhar/mover configuração sem vazar
    /// credenciais.
    /// </summary>
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

    /// <summary>
    /// Importa configuração de um JSON, validando via <see cref="AppOptions.Validate"/> antes de
    /// aplicar (salvar). Campos redigidos (<c>"***REDACTED***"</c>) são ignorados, preservando o
    /// valor atual do segredo em vez de sobrescrevê-lo com o placeholder.
    /// </summary>
    /// <exception cref="AppOptionsValidationException">Quando o JSON importado é inválido.</exception>
    public AppOptions ImportConfig(string json)
    {
        var incoming = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
                       ?? throw new JsonException("JSON de configuração vazio ou inválido.");
        incoming.Remove("_aviso");

        var current = Load();
        var currentDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            JsonSerializer.Serialize(current, JsonOptions))!;

        // Mescla: valores importados sobrescrevem os atuais, exceto placeholders redigidos.
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

        Save(merged); // Save já chama Validate().
        return merged;
    }
}
