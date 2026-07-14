using System.Text.Json;
using QuestResume.Core.Models;

namespace QuestResume.Core.Persistence;

/// <summary>
/// Persists the list of registered <see cref="WebhookConfig"/> as a JSON sidecar
/// (<c>webhooks.json</c>) inside the index folder, following the same pattern as
/// <see cref="IndexManifestRepository"/> / <see cref="TagStoreRepository"/>.
/// </summary>
public sealed class WebhookStore
{
    public const string FileName = "webhooks.json";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _indexPath;

    public WebhookStore(string indexPath)
    {
        _indexPath = indexPath;
    }

    private string FilePath => Path.Combine(_indexPath, FileName);

    public List<WebhookConfig> Load()
    {
        if (!File.Exists(FilePath))
        {
            return new List<WebhookConfig>();
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<WebhookStoreData>(json, SerializerOptions)?.Webhooks
                   ?? new List<WebhookConfig>();
        }
        catch
        {
            return new List<WebhookConfig>();
        }
    }

    public void Save(List<WebhookConfig> webhooks)
    {
        Directory.CreateDirectory(_indexPath);
        var json = JsonSerializer.Serialize(new WebhookStoreData { Webhooks = webhooks }, SerializerOptions);
        File.WriteAllText(FilePath, json);
    }

    public void Add(WebhookConfig webhook)
    {
        var webhooks = Load();
        webhooks.RemoveAll(w => w.Url.Equals(webhook.Url, StringComparison.OrdinalIgnoreCase));
        webhooks.Add(webhook);
        Save(webhooks);
    }

    public bool Remove(string url)
    {
        var webhooks = Load();
        var removed = webhooks.RemoveAll(w => w.Url.Equals(url, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed)
        {
            Save(webhooks);
        }

        return removed;
    }
}
