using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace QuestResume.Core.Embeddings;

/// <summary>
/// Gera embeddings visuais (CLIP) para imagens, usados em busca por similaridade de imagem
/// (<see cref="QuestResume.Core.Indexing.SearchService.SearchByImageAsync"/>). Segue o mesmo
/// padrão de lazy-init de <see cref="EmbeddingService"/> e <see cref="CrossEncoderService"/>:
/// a sessão ONNX só é carregada no primeiro uso, e uma falha de configuração lança
/// <see cref="ClipNotConfiguredException"/> em vez de derrubar o processo.
///
/// LIMITAÇÃO TÉCNICA: sem um modelo ONNX CLIP real (com torre de imagem) configurado pelo
/// usuário via <see cref="Configuration.AppOptions.ClipModelPath"/>, este serviço não pode
/// gerar embeddings — o comportamento esperado é lançar <see cref="ClipNotConfiguredException"/>,
/// coberto por teste. O pré-processamento de imagem aqui é um resize+normalize simplificado
/// (224x224, normalização ImageNet) compatível com a maioria dos exports padrão do CLIP; um
/// modelo com pré-processamento diferente pode exigir ajustes.
/// </summary>
public sealed class ClipEmbeddingService : IClipEmbeddingService
{
    private const int ImageSize = 224;

    private readonly string _modelPath;
    private readonly object _initLock = new();

    private InferenceSession? _session;
    private bool _initAttempted;

    public ClipEmbeddingService(string modelPath)
    {
        _modelPath = modelPath;
    }

    /// <exception cref="ClipNotConfiguredException">Quando o modelo CLIP não está configurado ou falha ao carregar.</exception>
    public Task<float[]> EmbedImageAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException("Arquivo de imagem não encontrado.", imagePath);
        }

        var input = ImagePreprocessor.LoadAndPreprocess(imagePath, ImageSize);

        var session = _session!;
        return Task.Run(() =>
        {
            var inputName = session.InputMetadata.Keys.First();
            var tensor = new DenseTensor<float>(input, new[] { 1, 3, ImageSize, ImageSize });
            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(inputName, tensor) };

            using var results = session.Run(inputs);
            var output = results.First().AsTensor<float>();
            return Normalize(output.ToArray());
        }, cancellationToken);
    }

    private static float[] Normalize(float[] embedding)
    {
        var norm = MathF.Sqrt(embedding.Sum(v => v * v));
        if (norm <= 0f) return embedding;
        var result = new float[embedding.Length];
        for (var i = 0; i < embedding.Length; i++) result[i] = embedding[i] / norm;
        return result;
    }

    private void EnsureInitialized()
    {
        if (_session is not null) return;

        lock (_initLock)
        {
            if (_session is not null) return;

            if (_initAttempted)
            {
                throw new ClipNotConfiguredException(_modelPath);
            }

            _initAttempted = true;

            if (string.IsNullOrWhiteSpace(_modelPath) || !File.Exists(_modelPath))
            {
                throw new ClipNotConfiguredException(_modelPath);
            }

            try
            {
                _session = new InferenceSession(_modelPath);
            }
            catch (Exception ex)
            {
                throw new ClipNotConfiguredException(_modelPath, ex);
            }
        }
    }

    public void Dispose() => _session?.Dispose();
}
