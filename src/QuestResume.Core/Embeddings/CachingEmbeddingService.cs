using System.Collections.Concurrent;

namespace QuestResume.Core.Embeddings;

/// <summary>
/// Decorator de <see cref="IEmbeddingService"/> que memoiza o embedding de consultas repetidas
/// (mesma pergunta digitada de novo, HyDE, multi-query com variações iguais, etc.), evitando
/// recalcular o embedding — que exige uma passada de inferência ONNX. A chave é o texto
/// normalizado (trim + minúsculas). O cache tem tamanho máximo (<see cref="ConcurrentDictionary{TKey,TValue}"/>
/// + fila de ordem de inserção) com despejo LRU simples quando o limite é atingido.
/// </summary>
public sealed class CachingEmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingService _inner;
    private readonly int _maxEntries;
    private readonly ConcurrentDictionary<string, float[]> _cache = new();
    private readonly ConcurrentQueue<string> _order = new();

    public CachingEmbeddingService(IEmbeddingService inner, int maxEntries = 256)
    {
        _inner = inner;
        _maxEntries = Math.Max(1, maxEntries);
    }

    /// <summary>Número de entradas atualmente no cache (para testes/diagnóstico).</summary>
    public int Count => _cache.Count;

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var key = Normalize(text);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var embedding = await _inner.EmbedAsync(text, cancellationToken).ConfigureAwait(false);

        if (_cache.TryAdd(key, embedding))
        {
            _order.Enqueue(key);
            EvictIfNeeded();
        }

        return embedding;
    }

    private void EvictIfNeeded()
    {
        while (_cache.Count > _maxEntries && _order.TryDequeue(out var oldest))
        {
            _cache.TryRemove(oldest, out _);
        }
    }

    private static string Normalize(string text) => text.Trim().ToLowerInvariant();

    public void Dispose() => _inner.Dispose();
}
