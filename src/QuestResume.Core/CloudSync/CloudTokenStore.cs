using System.Text.Json;

namespace QuestResume.Core.CloudSync;

public sealed class CloudTokenStore
{
    public const string FileName = "cloud-tokens.json";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _indexPath;

    public CloudTokenStore(string indexPath)
    {
        _indexPath = indexPath;
    }

    private string FilePath => Path.Combine(_indexPath, FileName);

    public CloudAuthResult? Load(string providerName)
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            var all = JsonSerializer.Deserialize<Dictionary<string, CloudAuthResult>>(json, SerializerOptions)
                      ?? new Dictionary<string, CloudAuthResult>();
            return all.TryGetValue(providerName.ToLowerInvariant(), out var result) ? result : null;
        }
        catch
        {
            return null;
        }
    }

    public void Save(string providerName, CloudAuthResult authResult)
    {
        Directory.CreateDirectory(_indexPath);

        var all = new Dictionary<string, CloudAuthResult>();
        if (File.Exists(FilePath))
        {
            try
            {
                var json = File.ReadAllText(FilePath);
                all = JsonSerializer.Deserialize<Dictionary<string, CloudAuthResult>>(json, SerializerOptions)
                      ?? new Dictionary<string, CloudAuthResult>();
            }
            catch
            {
                all = new Dictionary<string, CloudAuthResult>();
            }
        }

        all[providerName.ToLowerInvariant()] = authResult;
        var updatedJson = JsonSerializer.Serialize(all, SerializerOptions);
        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, updatedJson);
        File.Move(tempPath, FilePath, overwrite: true);
    }
}
