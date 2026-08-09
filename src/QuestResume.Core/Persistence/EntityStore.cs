using System.Text.Json;
using QuestResume.Core.Models;
using QuestResume.Core.Rag;

namespace QuestResume.Core.Persistence;

public sealed class EntityStore
{
    public const string FileName = "entities.json";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private Dictionary<string, List<ExtractedEntity>> _byDocument;

    public EntityStore(string indexPath)
    {
        _filePath = Path.Combine(indexPath, FileName);
        _byDocument = Load();
    }

    private Dictionary<string, List<ExtractedEntity>> Load()
    {
        if (!File.Exists(_filePath)) return new(StringComparer.OrdinalIgnoreCase);
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, List<ExtractedEntity>>>(
                       File.ReadAllText(_filePath), SerializerOptions)
                   ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch { return new(StringComparer.OrdinalIgnoreCase); }
    }

    public void Save() => File.WriteAllText(_filePath, JsonSerializer.Serialize(_byDocument, SerializerOptions));

    public void SetEntities(string sourcePath, IReadOnlyList<ExtractedEntity> entities)
    {
        _byDocument[sourcePath] = entities.ToList();
        Save();
    }

    public IReadOnlyList<ExtractedEntity> GetEntities(string sourcePath) =>
        _byDocument.TryGetValue(sourcePath, out var list) ? list : Array.Empty<ExtractedEntity>();

        public IReadOnlyList<string> GetDocumentsMentioning(string entityName)
    {
        var name = entityName.Trim();
        return _byDocument
            .Where(kv => kv.Value.Any(e => string.Equals(e.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase)))
            .Select(kv => kv.Key)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

        public IReadOnlyDictionary<string, IReadOnlyList<string>> ToDocumentEntityMap() =>
        _byDocument.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value.Select(e => e.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList(),
            StringComparer.OrdinalIgnoreCase);

    public KnowledgeGraph BuildKnowledgeGraph() => KnowledgeGraph.Build(ToDocumentEntityMap());
}
