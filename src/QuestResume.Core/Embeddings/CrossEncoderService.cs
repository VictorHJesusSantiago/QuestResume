using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace QuestResume.Core.Embeddings;

public sealed class CrossEncoderService : ICrossEncoderService
{
        private const int MaxSequenceLength = 512;

    private readonly string _modelPath;
    private readonly string _vocabPath;
    private readonly object _initLock = new();

    private InferenceSession? _session;
    private BertTokenizer? _tokenizer;
    private bool _initAttempted;

    public CrossEncoderService(string modelPath, string vocabPath)
    {
        _modelPath = modelPath;
        _vocabPath = vocabPath;
    }

        public Task<float> ScoreAsync(string query, string passage, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        var queryIds = _tokenizer!.EncodeToIds(query, true, true);
        var passageIds = _tokenizer.EncodeToIds(passage, true, true);

        
        
        
        var combined = new List<int>(queryIds.Count + passageIds.Count - 1);
        combined.AddRange(queryIds);
        combined.AddRange(passageIds.Skip(1));

        if (combined.Count > MaxSequenceLength)
        {
            combined = combined.Take(MaxSequenceLength).ToList();
        }

        var inputIds = new long[combined.Count];
        var attentionMask = new long[combined.Count];
        var tokenTypeIds = new long[combined.Count];
        for (var i = 0; i < combined.Count; i++)
        {
            inputIds[i] = combined[i];
            attentionMask[i] = 1L;
            tokenTypeIds[i] = i < queryIds.Count ? 0L : 1L;
        }

        var dimensions = new[] { 1, combined.Count };

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(inputIds, dimensions, false)),
            NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(attentionMask, dimensions, false))
        };

        if (_session!.InputMetadata.ContainsKey("token_type_ids"))
        {
            inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>(tokenTypeIds, dimensions, false)));
        }

        using var results = _session.Run(inputs);
        var output = results.FirstOrDefault(r => r.Name == "logits") ?? results.First();
        var logits = output.AsTensor<float>();

        return Task.FromResult(ToScore(logits));
    }

        private static float ToScore(Tensor<float> logits)
    {
        var lastDim = logits.Dimensions[^1];

        if (lastDim <= 1)
        {
            var value = logits.Length > 0 ? logits[0, 0] : 0f;
            return 1f / (1f + MathF.Exp(-value));
        }

        var negative = logits[0, 0];
        var positive = logits[0, 1];
        var maxLogit = MathF.Max(negative, positive);
        var expNegative = MathF.Exp(negative - maxLogit);
        var expPositive = MathF.Exp(positive - maxLogit);
        return expPositive / (expNegative + expPositive);
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
                throw new RerankingNotConfiguredException(_modelPath, _vocabPath);
            }

            _initAttempted = true;

            if (string.IsNullOrWhiteSpace(_modelPath) || !File.Exists(_modelPath)
                || string.IsNullOrWhiteSpace(_vocabPath) || !File.Exists(_vocabPath))
            {
                throw new RerankingNotConfiguredException(_modelPath, _vocabPath);
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
                throw new RerankingNotConfiguredException(_modelPath, _vocabPath, ex);
            }
        }
    }

    public void Dispose() => _session?.Dispose();
}
