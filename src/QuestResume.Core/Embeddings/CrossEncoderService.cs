using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace QuestResume.Core.Embeddings;

/// <summary>
/// Scores how relevant a passage is to a query using a local ONNX cross-encoder model and a
/// WordPiece tokenizer (vocab.txt), via <see cref="Microsoft.ML.OnnxRuntime"/> and
/// <see cref="BertTokenizer"/>. Used by <see cref="QuestResume.Core.Indexing.HybridSearchService"/>
/// to re-rank the top candidates returned by BM25/vector search.
/// </summary>
public sealed class CrossEncoderService : IDisposable
{
    /// <summary>
    /// Max combined (query + passage + special tokens) input sequence length, mirroring
    /// <see cref="EmbeddingService"/>. Longer inputs are truncated before being sent to the
    /// ONNX model to avoid a shape-mismatch failure in <see cref="InferenceSession.Run"/>.
    /// </summary>
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

    /// <summary>
    /// Computes a relevance score (higher = more relevant) for <paramref name="passage"/> given
    /// <paramref name="query"/>, using BERT pair encoding: <c>[CLS] query [SEP] passage [SEP]</c>.
    /// The score is mapped to the 0-1 range (via sigmoid or softmax, depending on the model's
    /// output shape) so it can be combined with the normalized BM25/vector scores.
    /// </summary>
    /// <exception cref="RerankingNotConfiguredException">
    /// Thrown if the re-ranking model or vocabulary path is missing, invalid, or fails to load.
    /// </exception>
    public Task<float> ScoreAsync(string query, string passage, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        var queryIds = _tokenizer!.EncodeToIds(query, true, true);
        var passageIds = _tokenizer.EncodeToIds(passage, true, true);

        // Pair encoding: [CLS] query... [SEP] passage... [SEP]. queryIds already starts with
        // [CLS] and ends with [SEP]; passageIds[1..] drops its leading [CLS] but keeps its
        // trailing [SEP], giving the standard two-segment BERT layout.
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

    /// <summary>
    /// Converts the model's raw output to a 0-1 relevance score. Cross-encoders exported as a
    /// regression head produce a single logit per sequence (sigmoid applied); ones exported as a
    /// 2-class classifier ([not relevant, relevant]) produce two logits (softmax, take class 1).
    /// </summary>
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

    /// <summary>
    /// Lazily loads the ONNX session and tokenizer. Guarded by <see cref="_initLock"/> because
    /// this <see cref="CrossEncoderService"/> instance can be shared (via a cached RAG engine)
    /// across concurrent requests — without the lock, two threads racing through the
    /// uninitialized check could both construct an <see cref="InferenceSession"/>, leaking one.
    /// </summary>
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
