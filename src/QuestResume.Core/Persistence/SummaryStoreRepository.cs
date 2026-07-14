using System.Text.Json;
using QuestResume.Core.Models;

namespace QuestResume.Core.Persistence;

/// <summary>
/// Implementação em arquivo JSON de <see cref="ISummaryStoreRepository"/>. Retorna um store
/// vazio quando o sidecar está ausente ou corrompido, mesmo padrão de <see cref="TagStoreRepository"/>.
/// </summary>
public sealed class SummaryStoreRepository : ISummaryStoreRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _indexPath;

    public SummaryStoreRepository(string indexPath)
    {
        _indexPath = indexPath;
    }

    private string FilePath => Path.Combine(_indexPath, SummaryStore.FileName);

    public SummaryStore Load()
    {
        if (!File.Exists(FilePath)) return new SummaryStore();
        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<SummaryStore>(json, SerializerOptions) ?? new SummaryStore();
        }
        catch { return new SummaryStore(); }
    }

    public void Save(SummaryStore store)
    {
        Directory.CreateDirectory(_indexPath);
        var json = JsonSerializer.Serialize(store, SerializerOptions);
        File.WriteAllText(FilePath, json);
    }
}
