using System.Text.Json;
using QuestResume.Core.Models;

namespace QuestResume.Core.Persistence;

/// <summary>
/// Anotações e destaques do usuário por documento, persistidos num sidecar JSON
/// <c>annotations.json</c> ao lado do índice. Preservado entre reindexações (é metadado do usuário,
/// como <see cref="TagStore"/>).
/// </summary>
public sealed class AnnotationStore
{
    public const string FileName = "annotations.json";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private List<Annotation> _annotations;

    public AnnotationStore(string indexPath)
    {
        _filePath = Path.Combine(indexPath, FileName);
        _annotations = Load();
    }

    private List<Annotation> Load()
    {
        if (!File.Exists(_filePath)) return new List<Annotation>();
        try
        {
            return JsonSerializer.Deserialize<List<Annotation>>(File.ReadAllText(_filePath), SerializerOptions)
                   ?? new List<Annotation>();
        }
        catch { return new List<Annotation>(); }
    }

    public void Save() => File.WriteAllText(_filePath, JsonSerializer.Serialize(_annotations, SerializerOptions));

    public Annotation Add(Annotation annotation)
    {
        _annotations.Add(annotation);
        Save();
        return annotation;
    }

    /// <summary>Remove uma anotação por id. Retorna <c>true</c> se algo foi removido.</summary>
    public bool Remove(string id)
    {
        var removed = _annotations.RemoveAll(a => a.Id == id) > 0;
        if (removed) Save();
        return removed;
    }

    public IReadOnlyList<Annotation> GetForDocument(string sourcePath) =>
        _annotations
            .Where(a => string.Equals(a.SourcePath, sourcePath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.StartOffset)
            .ToList();

    public IReadOnlyList<Annotation> GetAll() => _annotations.ToList();
}
