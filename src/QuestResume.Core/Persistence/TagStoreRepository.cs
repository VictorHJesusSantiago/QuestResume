using System.Text.Json;
using QuestResume.Core.Models;

namespace QuestResume.Core.Persistence;

/// <summary>
/// File-backed implementation of <see cref="ITagStoreRepository"/>. Returns an empty store
/// when the JSON sidecar is missing or corrupt.
/// </summary>
public sealed class TagStoreRepository : ITagStoreRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _indexPath;

    public TagStoreRepository(string indexPath)
    {
        _indexPath = indexPath;
    }

    private string FilePath => Path.Combine(_indexPath, TagStore.FileName);

    public TagStore Load()
    {
        if (!File.Exists(FilePath)) return new TagStore();
        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<TagStore>(json, SerializerOptions) ?? new TagStore();
        }
        catch { return new TagStore(); }
    }

    public void Save(TagStore store)
    {
        var json = JsonSerializer.Serialize(store, SerializerOptions);
        File.WriteAllText(FilePath, json);
    }
}
