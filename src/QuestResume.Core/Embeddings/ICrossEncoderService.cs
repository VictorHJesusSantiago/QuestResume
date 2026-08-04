namespace QuestResume.Core.Embeddings;

public interface ICrossEncoderService : IDisposable
{
        Task<float> ScoreAsync(string query, string passage, CancellationToken cancellationToken = default);
}
