using System.Text.Json;
using QuestResume.Core.Models;

namespace QuestResume.Core.Persistence;

/// <summary>
/// File-backed implementation of <see cref="IIndexManifestRepository"/>. Returns an empty
/// manifest when the JSON sidecar is missing or corrupt.
/// </summary>
public sealed class IndexManifestRepository : IIndexManifestRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _indexPath;

    public IndexManifestRepository(string indexPath)
    {
        _indexPath = indexPath;
    }

    private string FilePath => Path.Combine(_indexPath, IndexManifest.FileName);

    public IndexManifest Load()
    {
        if (!File.Exists(FilePath)) return new IndexManifest();
        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<IndexManifest>(json, SerializerOptions) ?? new IndexManifest();
        }
        catch { return new IndexManifest(); }
    }

    public void Save(IndexManifest manifest)
    {
        var json = JsonSerializer.Serialize(manifest, SerializerOptions);
        File.WriteAllText(FilePath, json);
    }
}
