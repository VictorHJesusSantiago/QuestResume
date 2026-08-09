using QuestResume.Core.Extraction;
using QuestResume.Core.Models;

namespace QuestResume.Core.Extraction.Extractors;

public sealed class PlainTextExtractor : IFileExtractor
{
    private readonly long _streamingThresholdBytes;

        public PlainTextExtractor(long streamingThresholdBytes = 50L * 1024 * 1024)
    {
        _streamingThresholdBytes = streamingThresholdBytes;
    }

    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[]
    {
        ".txt", ".csv", ".css", ".js", ".json", ".xml", ".bib", ".tex", ".ics", ".vcf",
        
        
        ".cs", ".py", ".java", ".ts", ".tsx", ".jsx", ".go", ".rb", ".php", ".c", ".cpp",
        ".h", ".hpp", ".rs", ".kt", ".swift", ".sh", ".ps1", ".sql", ".yaml", ".yml", ".md",
        
        
        ".reg"
    };

    public async Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);

        
        
        
        
        
        
        
        string text;
        if (info.Length > _streamingThresholdBytes)
        {
            text = await ReadLargeFileStreamedAsync(path, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            text = await EncodingDetector.ReadAllTextDetectedAsync(path, cancellationToken).ConfigureAwait(false);
        }

        return new ExtractedDocument
        {
            Path = path,
            FileName = info.Name,
            Extension = info.Extension,
            Text = text,
            ModifiedUtc = info.LastWriteTimeUtc
        };
    }

    private static async Task<string> ReadLargeFileStreamedAsync(string path, CancellationToken cancellationToken)
    {
        const int bufferSize = 128 * 1024;
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
        using var reader = new StreamReader(
            stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize);

        var sb = new System.Text.StringBuilder();
        var buffer = new char[bufferSize];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            sb.Append(buffer, 0, read);
        }

        return sb.ToString();
    }
}
