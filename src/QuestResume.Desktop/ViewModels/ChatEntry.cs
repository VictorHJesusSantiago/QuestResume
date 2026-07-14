namespace QuestResume.Desktop.ViewModels;

public sealed class ChatEntry
{
    public required string Role { get; init; }
    public required string Text { get; init; }
    public IReadOnlyList<SourceReference>? Sources { get; init; }

    /// <summary>
    /// Sugestões de perguntas relacionadas (ver <see cref="QuestResume.Core.Models.AskResult.RelatedQuestions"/>),
    /// mostradas como botões pequenos abaixo da resposta. Vazia para mensagens que não sejam a
    /// resposta mais recente do LLM.
    /// </summary>
    public IReadOnlyList<string> RelatedQuestions { get; init; } = Array.Empty<string>();
}

/// <summary>
/// A clickable reference to an indexed source file, shown below a chat answer so the user can
/// open the original document via <see cref="MainViewModel.OpenSourceCommand"/>.
/// </summary>
public sealed class SourceReference
{
    public required string FileName { get; init; }
    public required string SourcePath { get; init; }

    /// <summary>
    /// Index (within the source document) of the chunk actually used to answer the question,
    /// used by <see cref="MainViewModel.ViewSourceChunkCommand"/> to show the exact passage.
    /// </summary>
    public int ChunkIndex { get; init; }
}
