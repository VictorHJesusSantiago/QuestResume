namespace QuestResume.Core.Embeddings;

/// <summary>
/// Thrown when a chunk's embedding is requested but no usable ONNX model/vocabulary has been
/// configured. Full-text (BM25) search keeps working without embeddings; this exception only
/// affects the optional hybrid-search and indexing-time embedding steps.
/// </summary>
public sealed class EmbeddingsNotConfiguredException : Exception
{
    public EmbeddingsNotConfiguredException(string modelPath, string vocabPath, Exception? innerException = null)
        : base(BuildMessage(modelPath, vocabPath), innerException)
    {
    }

    private static string BuildMessage(string modelPath, string vocabPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || string.IsNullOrWhiteSpace(vocabPath))
        {
            return "Embeddings não configurados. Defina o caminho do modelo de embeddings em formato " +
                   "ONNX (EmbeddingModelPath) e do vocabulário WordPiece (EmbeddingTokenizerPath, " +
                   "arquivo vocab.txt) nas configurações. Modelo recomendado: " +
                   "'sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2' exportado para ONNX.";
        }

        return $"Não foi possível carregar o modelo de embeddings em '{modelPath}' ou o vocabulário " +
               $"em '{vocabPath}'. Verifique se os caminhos configurados apontam para um modelo ONNX " +
               "válido e um arquivo vocab.txt compatível.";
    }
}
