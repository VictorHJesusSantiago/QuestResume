using QuestResume.Core.Extraction;
using QuestResume.Core.Models;

namespace QuestResume.SamplePlugin;

public sealed class LogFileExtractor : IExtractorPlugin
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".log" };

    public async Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);

        return new ExtractedDocument
        {
            Path = path,
            FileName = info.Name,
            Extension = info.Extension,
            Text = text,
            ModifiedUtc = info.LastWriteTimeUtc,
            Metadata = new Dictionary<string, string>
            {
                ["plugin"] = nameof(LogFileExtractor)
            }
        };
    }
}
