using QuestResume.Core.Models;

namespace QuestResume.Core.Extraction;

/// <summary>
/// Extracts text (and optionally metadata) from a single file format or group of formats.
/// </summary>
public interface IFileExtractor
{
    /// <summary>
    /// The file extensions this extractor handles, including the leading dot, lower case
    /// (e.g. ".pdf", ".docx").
    /// </summary>
    IReadOnlyCollection<string> SupportedExtensions { get; }

    Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default);
}
