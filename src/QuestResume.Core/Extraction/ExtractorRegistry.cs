using QuestResume.Core.Configuration;
using QuestResume.Core.Extraction.Extractors;
using QuestResume.Core.Models;

namespace QuestResume.Core.Extraction;

/// <summary>
/// Maps file extensions to the <see cref="IFileExtractor"/> able to read them, and provides
/// a single entry point for extracting a document regardless of its format.
/// </summary>
public sealed class ExtractorRegistry
{
    private readonly Dictionary<string, IFileExtractor> _extractorsByExtension;

    public ExtractorRegistry(IEnumerable<IFileExtractor>? extractors = null)
    {
        extractors ??= DefaultExtractors();

        _extractorsByExtension = new Dictionary<string, IFileExtractor>(StringComparer.OrdinalIgnoreCase);
        foreach (var extractor in extractors)
        {
            foreach (var extension in extractor.SupportedExtensions)
            {
                _extractorsByExtension[extension] = extractor;
            }
        }
    }

    /// <summary>
    /// Default set of extractors covering Group 1 (direct text extraction) formats, plus the
    /// OCR-based extractors from <paramref name="options"/> when <c>OcrEnabled</c> is true.
    /// With <paramref name="options"/> <c>null</c> (or OCR disabled), the result is identical
    /// to the original Group 1-only set.
    /// </summary>
    public static IEnumerable<IFileExtractor> DefaultExtractors(AppOptions? options = null)
    {
        yield return new PlainTextExtractor();
        yield return new IpynbExtractor();

        if (options?.OcrEnabled == true)
        {
            yield return new PdfExtractor(ocrEnabled: true, options.TessDataPath, options.OcrLanguages);
            yield return new ImageOcrExtractor(options.TessDataPath, options.OcrLanguages);
        }
        else
        {
            yield return new PdfExtractor();
        }

        yield return new OpenXmlExtractor();
        yield return new HtmlExtractor();
        yield return new OdtExtractor();
        yield return new RtfExtractor();
        yield return new EpubExtractor();
        yield return new EmailExtractor();
    }

    public IReadOnlyCollection<string> SupportedExtensions => _extractorsByExtension.Keys;

    public bool IsSupported(string extension) => _extractorsByExtension.ContainsKey(extension);

    /// <summary>
    /// Extracts the document at <paramref name="path"/>. If no extractor is registered for the
    /// file's extension, returns a document with empty text and a note in <c>Metadata["warning"]</c>
    /// rather than throwing, so a single unsupported file doesn't abort an indexing run.
    /// </summary>
    public async Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(path);
        var info = new FileInfo(path);

        if (_extractorsByExtension.TryGetValue(extension, out var extractor))
        {
            return await extractor.ExtractAsync(path, cancellationToken).ConfigureAwait(false);
        }

        return new ExtractedDocument
        {
            Path = path,
            FileName = info.Name,
            Extension = extension,
            Text = string.Empty,
            ModifiedUtc = info.LastWriteTimeUtc,
            Metadata = new Dictionary<string, string>
            {
                ["warning"] = $"Nenhum extrator registrado para a extensão '{extension}'."
            }
        };
    }
}
