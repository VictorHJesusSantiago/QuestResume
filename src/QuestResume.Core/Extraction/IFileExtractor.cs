using QuestResume.Core.Models;

namespace QuestResume.Core.Extraction;

public interface IFileExtractor
{
        IReadOnlyCollection<string> SupportedExtensions { get; }

    Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default);
}
