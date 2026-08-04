using QuestResume.Core.Configuration;
using QuestResume.Core.Extraction.Extractors;
using QuestResume.Core.Models;

namespace QuestResume.Core.Extraction;

public sealed class ExtractorRegistry
{
    private readonly Dictionary<string, IFileExtractor> _extractorsByExtension;
    private readonly List<LoadedPluginInfo> _loadedPlugins = new();

    public ExtractorRegistry(IEnumerable<IFileExtractor>? extractors = null, bool loadPlugins = false, Action<string>? pluginLog = null)
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

        if (loadPlugins)
        {
            RegisterPlugins(PluginLoader.LoadPlugins(log: pluginLog), pluginLog);
        }
    }

        public void RegisterPlugins(IEnumerable<IFileExtractor> pluginExtractors, Action<string>? log = null)
    {
        foreach (var extractor in pluginExtractors)
        {
            foreach (var extension in extractor.SupportedExtensions)
            {
                _extractorsByExtension[extension] = extractor;
            }

            _loadedPlugins.Add(new LoadedPluginInfo
            {
                AssemblyFileName = extractor.GetType().Assembly.GetName().Name ?? extractor.GetType().Assembly.FullName ?? "desconhecido",
                ExtractorTypeName = extractor.GetType().FullName ?? extractor.GetType().Name,
                SupportedExtensions = extractor.SupportedExtensions
            });
        }
    }

        public IReadOnlyList<LoadedPluginInfo> LoadedPlugins => _loadedPlugins;

        public static IEnumerable<IFileExtractor> DefaultExtractors(AppOptions? options = null, bool includeArchives = true)
    {
        yield return new PlainTextExtractor();
        yield return new IpynbExtractor();
        yield return new SubtitleExtractor();

        if (options?.OcrEnabled == true)
        {
            yield return new PdfExtractor(ocrEnabled: true, options.TessDataPath, options.OcrLanguages);
            yield return new ImageOcrExtractor(options.TessDataPath, options.OcrLanguages);
            yield return new OpenXmlExtractor(ocrEnabled: true, options.TessDataPath, options.OcrLanguages);
        }
        else
        {
            yield return new PdfExtractor();
            yield return new OpenXmlExtractor();
        }
        yield return new HtmlExtractor();
        yield return new OdtExtractor();
        yield return new OdsExtractor();
        yield return new OdpExtractor();
        yield return new IWorkExtractor();
        yield return new RtfExtractor();
        yield return new EpubExtractor();
        yield return new EmailExtractor();
        yield return new VideoMetadataExtractor();
        yield return new ExecutableMetadataExtractor();
        yield return new LegacyOfficeExtractor();
        yield return new SqliteExtractor();
        yield return new ParquetExtractor();
        yield return new Fb2Extractor();
        yield return new MobiExtractor();
        yield return new ChmDjvuMetadataExtractor();
        yield return new LnkExtractor();
        yield return new PsdExtractor();
        yield return new ApkExtractor();
        yield return new TorrentExtractor();
        yield return new DwgDxfExtractor();
        yield return new PstOstExtractor();

        if (options?.SttEnabled == true)
        {
            yield return new AudioTranscriptionExtractor(options.WhisperModelPath);
        }

        if (includeArchives)
        {
            yield return new ZipArchiveExtractor(options);
            yield return new ArchiveExtractor(options);
        }
    }

    public IReadOnlyCollection<string> SupportedExtensions => _extractorsByExtension.Keys;

    public bool IsSupported(string extension) => _extractorsByExtension.ContainsKey(extension);

        public async Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(path);
        var info = new FileInfo(path);

        if (_extractorsByExtension.TryGetValue(extension, out var extractor))
        {
            try
            {
                return await extractor.ExtractAsync(path, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw; 
            }
            catch (Exception ex)
            {
                
                
                
                
                return new ExtractedDocument
                {
                    Path = path,
                    FileName = info.Name,
                    Extension = extension,
                    Text = string.Empty,
                    ModifiedUtc = info.LastWriteTimeUtc,
                    Metadata = new Dictionary<string, string>
                    {
                        ["error"] = $"Falha ao extrair '{info.Name}': {ex.GetType().Name}: {ex.Message}"
                    }
                };
            }
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
