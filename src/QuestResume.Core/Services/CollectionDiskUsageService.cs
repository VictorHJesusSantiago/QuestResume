using QuestResume.Core.Persistence;

namespace QuestResume.Core.Services;

/// <summary>Uso de disco (em bytes) de uma coleção.</summary>
public sealed class CollectionDiskUsage
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public long SizeBytes { get; init; }
    public double SizeMb => Math.Round(SizeBytes / (1024d * 1024d), 2);
}

/// <summary>
/// Calcula o tamanho em disco de cada coleção (<see cref="CollectionStore.List"/> + soma recursiva
/// de <see cref="FileInfo.Length"/> por pasta de coleção), reaproveitando a mesma lógica que o
/// dashboard já usa para o índice inteiro. Usado pelo endpoint <c>GET /api/collections/disk-usage</c>.
/// </summary>
public sealed class CollectionDiskUsageService
{
    private readonly ICollectionStore _collectionStore;

    public CollectionDiskUsageService(ICollectionStore collectionStore)
    {
        _collectionStore = collectionStore;
    }

    public IReadOnlyList<CollectionDiskUsage> Compute()
    {
        return _collectionStore.List()
            .Select(c => new CollectionDiskUsage
            {
                Name = c.Nome,
                Path = c.Caminho,
                SizeBytes = GetDirectorySize(c.Caminho)
            })
            .OrderByDescending(u => u.SizeBytes)
            .ToList();
    }

    internal static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        long total = 0;
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            try { total += new FileInfo(file).Length; }
            catch { }
        }
        return total;
    }
}
