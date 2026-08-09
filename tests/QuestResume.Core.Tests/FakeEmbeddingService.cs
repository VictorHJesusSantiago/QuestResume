using QuestResume.Core.Embeddings;

namespace QuestResume.Core.Tests;

public sealed class FakeEmbeddingService(IReadOnlyList<string> keywords) : IEmbeddingService
{
    public List<string> EmbeddedTexts { get; } = new();

    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        EmbeddedTexts.Add(text);

        var lower = text.ToLowerInvariant();
        var vector = new float[keywords.Count + 1];

        for (var i = 0; i < keywords.Count; i++)
        {
            var count = 0;
            var index = 0;
            while ((index = lower.IndexOf(keywords[i], index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += keywords[i].Length;
            }
            vector[i] = count;
        }

        
        
        vector[^1] = 1f;

        return Task.FromResult(vector);
    }

    public void Dispose()
    {
    }
}
