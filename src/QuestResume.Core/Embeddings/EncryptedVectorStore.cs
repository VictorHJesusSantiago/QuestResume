using QuestResume.Core.Models;
using QuestResume.Core.Security;

namespace QuestResume.Core.Embeddings;

/// <summary>
/// Decora um <see cref="IVectorStore"/> (tipicamente <see cref="VectorStore"/>) criptografando
/// o texto do chunk e o vetor de embedding com AES-256-GCM antes de repassá-los ao armazenamento
/// subjacente, e decriptando-os ao ler. A chave é derivada da senha mestre via PBKDF2 (ver
/// <see cref="Security.MasterKeyManager"/>) e nunca é persistida. Caminho, nome de arquivo e
/// índice do trecho permanecem em texto claro no SQLite (necessários para indexação/remoção por
/// caminho e busca por prefixo), mas o conteúdo do trecho e o vetor de embedding — o que de fato
/// permitiria reconstruir o conteúdo do documento — ficam ilegíveis em <c>vectors.db</c> sem a
/// senha mestre.
///
/// Como o armazenamento subjacente só enxerga bytes criptografados, ele não pode ranquear por
/// similaridade de cosseno sozinho: <see cref="Search"/> busca todas as entradas via
/// <see cref="IVectorStore.GetAllEntries"/>, decripta cada uma e pontua localmente.
/// </summary>
public sealed class EncryptedVectorStore : IVectorStore
{
    private readonly IVectorStore _inner;
    private readonly byte[] _key;

    public EncryptedVectorStore(IVectorStore inner, string masterPassword, byte[] salt)
    {
        _inner = inner;
        _key = MasterKeyManager.DeriveKey(masterPassword, salt);
    }

    public void Add(string sourcePath, string fileName, int chunkIndex, string text, float[] embedding)
    {
        var encryptedText = Convert.ToBase64String(AesCryptoHelper.EncryptString(text, _key));
        var encryptedEmbedding = EncryptEmbedding(embedding);
        _inner.Add(sourcePath, fileName, chunkIndex, encryptedText, encryptedEmbedding);
    }

    public IReadOnlyList<SearchResultItem> Search(float[] queryEmbedding, int topK)
    {
        var scored = new List<SearchResultItem>();
        foreach (var (item, encryptedEmbedding) in _inner.GetAllEntries())
        {
            var embedding = DecryptEmbedding(encryptedEmbedding);
            scored.Add(new SearchResultItem
            {
                SourcePath = item.SourcePath,
                FileName = item.FileName,
                ChunkIndex = item.ChunkIndex,
                ChunkText = DecryptText(item.ChunkText),
                Score = CosineSimilarity(queryEmbedding, embedding)
            });
        }

        return scored.OrderByDescending(i => i.Score).Take(topK).ToList();
    }

    public IReadOnlyList<(SearchResultItem Item, float[] Embedding)> GetAllEntries()
    {
        var result = new List<(SearchResultItem, float[])>();
        foreach (var (item, encryptedEmbedding) in _inner.GetAllEntries())
        {
            var decryptedItem = new SearchResultItem
            {
                SourcePath = item.SourcePath,
                FileName = item.FileName,
                ChunkIndex = item.ChunkIndex,
                ChunkText = DecryptText(item.ChunkText),
                Score = item.Score
            };
            result.Add((decryptedItem, DecryptEmbedding(encryptedEmbedding)));
        }

        return result;
    }

    private string DecryptText(string base64) => AesCryptoHelper.DecryptString(Convert.FromBase64String(base64), _key);

    private float[] EncryptEmbedding(float[] embedding)
    {
        var bytes = new byte[embedding.Length * sizeof(float)];
        Buffer.BlockCopy(embedding, 0, bytes, 0, bytes.Length);
        var encrypted = AesCryptoHelper.Encrypt(bytes, _key);
        return BytesToFloats(encrypted);
    }

    private float[] DecryptEmbedding(float[] encryptedAsFloats)
    {
        var bytes = FloatsToBytes(encryptedAsFloats);
        var plaintext = AesCryptoHelper.Decrypt(bytes, _key);
        var embedding = new float[plaintext.Length / sizeof(float)];
        Buffer.BlockCopy(plaintext, 0, embedding, 0, plaintext.Length);
        return embedding;
    }

    // The underlying VectorStore only knows how to persist float[] embeddings; to reuse it
    // unchanged for ciphertext we round-trip the AES-GCM payload (nonce+tag+ciphertext, plus a
    // 4-byte length prefix to recover the exact byte count after float-alignment padding)
    // through a byte<->float[] encoding.
    private static float[] BytesToFloats(byte[] bytes)
    {
        var padded = new byte[4 + ((bytes.Length + 3) / 4) * 4];
        BitConverter.GetBytes(bytes.Length).CopyTo(padded, 0);
        bytes.CopyTo(padded, 4);
        var floats = new float[padded.Length / sizeof(float)];
        Buffer.BlockCopy(padded, 0, floats, 0, padded.Length);
        return floats;
    }

    private static byte[] FloatsToBytes(float[] floats)
    {
        var padded = new byte[floats.Length * sizeof(float)];
        Buffer.BlockCopy(floats, 0, padded, 0, padded.Length);
        var length = BitConverter.ToInt32(padded, 0);
        return padded.AsSpan(4, length).ToArray();
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
        {
            return 0f;
        }

        float dot = 0f, normA = 0f, normB = 0f;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA == 0f || normB == 0f)
        {
            return 0f;
        }

        return dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }

    // Embeddings visuais (CLIP) são delegados sem criptografia adicional: ao contrário do texto
    // do trecho, um vetor CLIP isolado não permite reconstruir o conteúdo do documento, e manter
    // o caminho em claro aqui já é necessário para localizar a imagem original.
    public void AddImageEmbedding(string sourcePath, string fileName, float[] embedding) =>
        _inner.AddImageEmbedding(sourcePath, fileName, embedding);

    public void RemoveImageEmbedding(string sourcePath) => _inner.RemoveImageEmbedding(sourcePath);

    public IReadOnlyList<ImageSearchResultItem> SearchImages(float[] queryEmbedding, int topK) =>
        _inner.SearchImages(queryEmbedding, topK);

    public void RemoveBySourcePath(string sourcePath) => _inner.RemoveBySourcePath(sourcePath);

    public void Clear() => _inner.Clear();

    public void InvalidateCache() => _inner.InvalidateCache();

    public IDisposable BeginBatch() => _inner.BeginBatch();

    public void Dispose() => _inner.Dispose();
}
