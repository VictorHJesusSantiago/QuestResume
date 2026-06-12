using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using QuestResume.Core.Extraction;
using QuestResume.Core.Models;
using IODirectory = System.IO.Directory;

namespace QuestResume.Core.Indexing;

/// <summary>
/// Walks a folder, extracts text from every supported file and writes the resulting
/// chunks to a Lucene.NET full-text index on disk.
/// </summary>
public sealed class DocumentIndexer
{
    public const LuceneVersion MatchVersion = LuceneVersion.LUCENE_48;

    private readonly ExtractorRegistry _registry;

    public DocumentIndexer(ExtractorRegistry? registry = null)
    {
        _registry = registry ?? new ExtractorRegistry();
    }

    /// <summary>
    /// Indexes every supported file under <paramref name="folderPath"/> (recursively),
    /// replacing any existing index at <paramref name="indexPath"/>.
    /// </summary>
    public async Task<IndexStats> IndexFolderAsync(
        string folderPath,
        string indexPath,
        int chunkSize = 1000,
        int overlap = 150,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IODirectory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"Pasta não encontrada: {folderPath}");
        }

        IODirectory.CreateDirectory(indexPath);

        var stats = new IndexStats();

        using var directory = FSDirectory.Open(indexPath);
        using var analyzer = new StandardAnalyzer(MatchVersion);
        var config = new IndexWriterConfig(MatchVersion, analyzer)
        {
            OpenMode = OpenMode.CREATE
        };

        using var writer = new IndexWriter(directory, config);

        foreach (var filePath in IODirectory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var extension = Path.GetExtension(filePath);
            if (!_registry.IsSupported(extension))
            {
                stats.FilesSkipped++;
                stats.SkippedFiles.Add(filePath);
                continue;
            }

            try
            {
                var document = await _registry.ExtractAsync(filePath, cancellationToken).ConfigureAwait(false);
                var chunks = TextChunker.Chunk(document, chunkSize, overlap);

                foreach (var chunk in chunks)
                {
                    writer.AddDocument(ToLuceneDocument(chunk));
                }

                stats.FilesProcessed++;
                stats.ChunksIndexed += chunks.Count;
                progress?.Report($"Indexado: {document.FileName} ({chunks.Count} trecho(s))");
            }
            catch (Exception ex)
            {
                stats.Errors.Add($"{filePath}: {ex.Message}");
                progress?.Report($"Erro ao processar {filePath}: {ex.Message}");
            }
        }

        writer.Commit();
        return stats;
    }

    private static Document ToLuceneDocument(TextChunk chunk) => new()
    {
        new StringField("path", chunk.SourcePath, Field.Store.YES),
        new StringField("fileName", chunk.FileName, Field.Store.YES),
        new StoredField("chunkIndex", chunk.ChunkIndex.ToString()),
        new TextField("content", chunk.Text, Field.Store.YES),
        new StringField("modifiedUtc", chunk.ModifiedUtc.Ticks.ToString(), Field.Store.YES)
    };
}
