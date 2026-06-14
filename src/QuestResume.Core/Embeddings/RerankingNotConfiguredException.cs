namespace QuestResume.Core.Embeddings;

/// <summary>
/// Thrown when a cross-encoder re-ranking score is requested but no usable ONNX model/vocabulary
/// has been configured. Hybrid (BM25 + vector) search keeps working without re-ranking; this
/// exception only affects the optional re-ranking step.
/// </summary>
public sealed class RerankingNotConfiguredException : Exception
{
    public RerankingNotConfiguredException(string modelPath, string vocabPath, Exception? innerException = null)
        : base(BuildMessage(modelPath, vocabPath), innerException)
    {
    }

    private static string BuildMessage(string modelPath, string vocabPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || string.IsNullOrWhiteSpace(vocabPath))
        {
            return "Re-ranking não configurado. Defina o caminho do modelo cross-encoder em formato " +
                   "ONNX (RerankingModelPath) e do vocabulário WordPiece (RerankingTokenizerPath, " +
                   "arquivo vocab.txt) nas configurações. Modelo recomendado: " +
                   "'cross-encoder/ms-marco-MiniLM-L-6-v2' exportado para ONNX.";
        }

        return $"Não foi possível carregar o modelo de re-ranking em '{modelPath}' ou o vocabulário " +
               $"em '{vocabPath}'. Verifique se os caminhos configurados apontam para um modelo ONNX " +
               "cross-encoder válido e um arquivo vocab.txt compatível.";
    }
}
