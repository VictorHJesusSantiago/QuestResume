namespace QuestResume.Core.Embeddings;

/// <summary>
/// Lançada quando uma busca por similaridade de imagem é solicitada mas nenhum modelo CLIP
/// ONNX válido foi configurado (<see cref="Configuration.AppOptions.ClipModelPath"/>). Este é o
/// comportamento esperado sem um modelo CLIP real fornecido pelo usuário — busca textual (BM25 /
/// embeddings de texto) continua funcionando normalmente.
/// </summary>
public sealed class ClipNotConfiguredException : Exception
{
    public ClipNotConfiguredException(string modelPath, Exception? innerException = null)
        : base(BuildMessage(modelPath), innerException)
    {
    }

    private static string BuildMessage(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            return "Busca por similaridade de imagem (CLIP) não configurada. Defina o caminho de um " +
                   "modelo CLIP no formato ONNX em 'ClipModelPath' nas configurações (ex.: um export ONNX " +
                   "de 'openai/clip-vit-base-patch32'). Sem esse modelo, apenas busca textual está disponível.";
        }

        return $"Não foi possível carregar o modelo CLIP em '{modelPath}'. Verifique se o caminho aponta " +
               "para um modelo ONNX válido com uma torre de imagem (image encoder) compatível.";
    }
}
