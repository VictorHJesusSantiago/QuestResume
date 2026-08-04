using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace QuestResume.Core.Embeddings;

public sealed class EmbeddingService : IEmbeddingService
{
        private const int MaxSequenceLength = 512;

    private readonly string _modelPath;
    private readonly string _vocabPath;
    private readonly object _initLock = new();

    private InferenceSession? _session;
    private BertTokenizer? _tokenizer;
    private bool _initAttempted;

    public EmbeddingService(string modelPath, string vocabPath)
    {
        _modelPath = modelPath;
        _vocabPath = vocabPath;
    }

        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        var tokenIds = _tokenizer!.EncodeToIds(text, true, true);
        if (tokenIds.Count > MaxSequenceLength)
        {
            tokenIds = tokenIds.Take(MaxSequenceLength).ToList();
        }

        var inputIds = new long[tokenIds.Count];
        var attentionMask = new long[tokenIds.Count];
        for (var i = 0; i < tokenIds.Count; i++)
        {
            inputIds[i] = tokenIds[i];
            attentionMask[i] = 1L;
        }

        var dimensions = new[] { 1, tokenIds.Count };

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(inputIds, dimensions, false)),
            NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(attentionMask, dimensions, false))
        };

        if (_session!.InputMetadata.ContainsKey("token_type_ids"))
        {
            var tokenTypeIds = new long[tokenIds.Count];
            inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>(tokenTypeIds, dimensions, false)));
        }

        
        
        
        var session = _session!;
        var mask = attentionMask;
        return Task.Run(() =>
        {
            using var results = session.Run(inputs);
            var output = results.FirstOrDefault(r => r.Name == "last_hidden_state") ?? results.First();
            return MeanPoolAndNormalize(output.AsTensor<float>(), mask);
        }, cancellationToken);
    }

    private static float[] MeanPoolAndNormalize(Tensor<float> hiddenStates, long[] attentionMask)
    {
        var seqLen = hiddenStates.Dimensions[1];
        var hiddenSize = hiddenStates.Dimensions[2];

        var embedding = new float[hiddenSize];
        var maskSum = 0f;

        for (var i = 0; i < seqLen; i++)
        {
            var mask = attentionMask[i];
            maskSum += mask;
            for (var j = 0; j < hiddenSize; j++)
            {
                embedding[j] += hiddenStates[0, i, j] * mask;
            }
        }

        if (maskSum > 0f)
        {
            for (var j = 0; j < hiddenSize; j++)
            {
                embedding[j] /= maskSum;
            }
        }

        var norm = MathF.Sqrt(embedding.Sum(v => v * v));
        if (norm > 0f)
        {
            for (var j = 0; j < hiddenSize; j++)
            {
                embedding[j] /= norm;
            }
        }

        return embedding;
    }

        private void EnsureInitialized()
    {
        if (_session is not null && _tokenizer is not null)
        {
            return;
        }

        lock (_initLock)
        {
            if (_session is not null && _tokenizer is not null)
            {
                return;
            }

            if (_initAttempted)
            {
                throw new EmbeddingsNotConfiguredException(_modelPath, _vocabPath);
            }

            _initAttempted = true;

            if (string.IsNullOrWhiteSpace(_modelPath) || !File.Exists(_modelPath)
                || string.IsNullOrWhiteSpace(_vocabPath) || !File.Exists(_vocabPath))
            {
                throw new EmbeddingsNotConfiguredException(_modelPath, _vocabPath);
            }

            try
            {
                _session = new InferenceSession(_modelPath);
                _tokenizer = BertTokenizer.Create(_vocabPath, new BertOptions());
            }
            catch (Exception ex)
            {
                _session?.Dispose();
                _session = null;
                _tokenizer = null;
                throw new EmbeddingsNotConfiguredException(_modelPath, _vocabPath, ex);
            }
        }
    }

    public void Dispose() => _session?.Dispose();
}
