using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace QuestResume.Core.Embeddings;

/// <summary>
/// Generates sentence embeddings for text chunks using a local ONNX model and a WordPiece
/// tokenizer (vocab.txt), via <see cref="Microsoft.ML.OnnxRuntime"/> and
/// <see cref="BertTokenizer"/>. Used to populate and query the <see cref="VectorStore"/> for
/// hybrid (BM25 + vector) search.
/// </summary>
public sealed class EmbeddingService : IEmbeddingService
{
    /// <summary>
    /// Max input sequence length accepted by typical BERT-family encoders
    /// (<c>max_position_embeddings</c>). Longer chunks are truncated before being sent to the
    /// ONNX model — without this, a chunk tokenizing past this length can make
    /// <see cref="InferenceSession.Run"/> throw on a shape mismatch, aborting the whole file
    /// being indexed.
    /// </summary>
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

    /// <summary>
    /// Computes a normalized embedding vector for <paramref name="text"/>.
    /// </summary>
    /// <exception cref="EmbeddingsNotConfiguredException">
    /// Thrown if the embedding model or vocabulary path is missing, invalid, or fails to load.
    /// </exception>
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

        // ONNX Runtime InferenceSession.Run is CPU-bound and blocks the calling thread for
        // the duration of the inference pass. Task.Run offloads it to the thread pool so the
        // ASP.NET request thread is free to handle other requests while inference runs.
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

    /// <summary>
    /// Lazily loads the ONNX session and tokenizer. Guarded by <see cref="_initLock"/> because
    /// this <see cref="EmbeddingService"/> instance can be shared (via a singleton RAG engine)
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
