namespace QuestResume.Core.Configuration;

/// <summary>
/// Thrown by <see cref="AppOptions.Validate"/> when one or more settings have values that
/// would cause indexing or querying to fail later (e.g. a bad <c>ChunkOverlap</c>/<c>ChunkSize</c>
/// combination). Catching this early avoids per-file failures during <c>DocumentIndexer.IndexFolderAsync</c>.
/// </summary>
public sealed class AppOptionsValidationException : Exception
{
    public AppOptionsValidationException(string message) : base(message)
    {
    }
}
