namespace QuestResume.Core.Rag;

public interface ILlmProvider : IDisposable
{
        Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default);

        IAsyncEnumerable<string> CompleteStreamAsync(string prompt, CancellationToken cancellationToken = default);
}
