namespace QuestResume.Core.Rag.Agent;

public interface ITool
{
        string Name { get; }

        string Description { get; }

        Task<string> InvokeAsync(string input, CancellationToken cancellationToken = default);
}
