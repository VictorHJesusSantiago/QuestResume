using System.Text.Json;
using QuestResume.Core.Models;

namespace QuestResume.Core.Persistence;

/// <summary>
/// Implementação em arquivo JSON (<c>collections.json</c>) de <see cref="ICollectionStore"/>,
/// seguindo o mesmo padrão de sidecar de <see cref="TagStoreRepository"/>. Cada coleção tem seu
/// próprio caminho físico de índice em <c>&lt;baseIndexPath&gt;/collections/&lt;nome&gt;/</c>.
/// A coleção "default" é sempre implicitamente disponível, apontando para
/// <c>&lt;baseIndexPath&gt;/</c> diretamente (compatibilidade com instalações anteriores à
/// introdução de múltiplas coleções).
/// </summary>
public sealed class CollectionStore : ICollectionStore
{
    /// <summary>Nome reservado da coleção padrão, usada quando nenhuma é especificada.</summary>
    public const string DefaultName = "default";

    private const string FileName = "collections.json";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _baseIndexPath;

    public CollectionStore(string baseIndexPath)
    {
        _baseIndexPath = baseIndexPath;
    }

    private string FilePath => Path.Combine(_baseIndexPath, FileName);

    private List<Collection> LoadRaw()
    {
        if (!File.Exists(FilePath)) return new List<Collection>();
        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<Collection>>(json, SerializerOptions) ?? new List<Collection>();
        }
        catch
        {
            return new List<Collection>();
        }
    }

    private void SaveRaw(List<Collection> collections)
    {
        Directory.CreateDirectory(_baseIndexPath);
        var json = JsonSerializer.Serialize(collections, SerializerOptions);
        File.WriteAllText(FilePath, json);
    }

    public IReadOnlyList<Collection> List()
    {
        var collections = LoadRaw();
        if (!collections.Any(c => string.Equals(c.Nome, DefaultName, StringComparison.OrdinalIgnoreCase)))
        {
            collections.Insert(0, new Collection
            {
                Nome = DefaultName,
                Caminho = _baseIndexPath,
                DataCriacao = DateTime.UtcNow
            });
        }

        return collections
            .OrderBy(c => string.Equals(c.Nome, DefaultName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(c => c.Nome, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Collection Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("O nome da coleção não pode ser vazio.", nameof(name));
        }

        var normalized = name.Trim();

        if (string.Equals(normalized, DefaultName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A coleção 'default' já existe implicitamente e não pode ser recriada.");
        }

        var collections = LoadRaw();
        if (collections.Any(c => string.Equals(c.Nome, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Já existe uma coleção chamada '{normalized}'.");
        }

        var collection = new Collection
        {
            Nome = normalized,
            Caminho = Path.Combine(_baseIndexPath, "collections", normalized),
            DataCriacao = DateTime.UtcNow
        };

        Directory.CreateDirectory(collection.Caminho);

        collections.Add(collection);
        SaveRaw(collections);
        return collection;
    }

    public bool Delete(string name)
    {
        if (string.Equals(name, DefaultName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A coleção 'default' não pode ser removida.");
        }

        var collections = LoadRaw();
        var removed = collections.RemoveAll(c => string.Equals(c.Nome, name, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
        {
            SaveRaw(collections);
        }

        return removed > 0;
    }

    public string ResolvePath(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, DefaultName, StringComparison.OrdinalIgnoreCase))
        {
            return _baseIndexPath;
        }

        var existing = LoadRaw().FirstOrDefault(c => string.Equals(c.Nome, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing.Caminho;
        }

        // Auto-cria a coleção na primeira referência (ex.: comando "index --collection novo"
        // antes de "collection create novo"), evitando um passo manual obrigatório.
        return Create(name).Caminho;
    }
}
