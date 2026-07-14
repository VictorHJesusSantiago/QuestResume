using QuestResume.Core.Models;

namespace QuestResume.Core.Embeddings;

/// <summary>
/// Persists chunk embeddings and performs approximate nearest-neighbour (ANN) search over
/// them. Abstracts <see cref="VectorStore"/> (SQLite + in-memory cosine search) so callers
/// are not coupled to SQLite or any specific ANN implementation.
/// </summary>
public interface IVectorStore : IDisposable
{
    /// <summary>Inserts a chunk embedding. Call <see cref="BeginBatch"/> before bulk inserts.</summary>
    void Add(string sourcePath, string fileName, int chunkIndex, string text, float[] embedding);

    /// <summary>Returns the <paramref name="topK"/> most similar chunks by cosine similarity.</summary>
    IReadOnlyList<SearchResultItem> Search(float[] queryEmbedding, int topK);

    /// <summary>Removes every stored embedding for <paramref name="sourcePath"/>.</summary>
    void RemoveBySourcePath(string sourcePath);

    /// <summary>Removes every stored embedding (used at the start of a full re-index).</summary>
    void Clear();

    /// <summary>
    /// Drops the in-memory cache so the next <see cref="Search"/> re-reads from the
    /// underlying store after a re-index performed by a different instance.
    /// </summary>
    void InvalidateCache();

    /// <summary>
    /// Wraps subsequent <see cref="Add"/> calls in a single transaction for bulk performance.
    /// Dispose the returned handle to commit.
    /// </summary>
    IDisposable BeginBatch();

    /// <summary>
    /// Returns every stored chunk with its raw embedding, bypassing similarity scoring. Used by
    /// <see cref="EncryptedVectorStore"/> to decrypt entries before ranking locally, since the
    /// underlying store only ever sees ciphertext and cannot score it meaningfully.
    /// </summary>
    IReadOnlyList<(SearchResultItem Item, float[] Embedding)> GetAllEntries();

    // --- Embeddings visuais (CLIP) — tabela separada de "chunks" de texto ---

    /// <summary>Insere/atualiza o embedding visual (CLIP) de uma imagem indexada.</summary>
    void AddImageEmbedding(string sourcePath, string fileName, float[] embedding);

    /// <summary>Remove o embedding visual armazenado para <paramref name="sourcePath"/>, se houver.</summary>
    void RemoveImageEmbedding(string sourcePath);

    /// <summary>
    /// Retorna os <paramref name="topK"/> arquivos de imagem mais similares (cosseno) a
    /// <paramref name="queryEmbedding"/>.
    /// </summary>
    IReadOnlyList<ImageSearchResultItem> SearchImages(float[] queryEmbedding, int topK);
}
