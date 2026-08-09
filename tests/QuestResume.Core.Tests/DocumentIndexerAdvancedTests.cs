using System.Text;
using QuestResume.Core.Extraction;
using QuestResume.Core.Indexing;

namespace QuestResume.Core.Tests;

public class DocumentIndexerAdvancedTests
{
    

    [Fact]
    public void EncodingDetector_DecodesCp1252BytesCorrectly()
    {
        
        
        var cp1252 = Encoding.GetEncoding(1252);
        var originalText = "café résumé açaí";
        var bytes = cp1252.GetBytes(originalText);

        var (_, decoded) = EncodingDetector.DetectAndDecode(bytes);

        Assert.Equal(originalText, decoded);
    }

    [Fact]
    public void EncodingDetector_DecodesValidUtf8AsUtf8()
    {
        var originalText = "café résumé açaí — em UTF-8";
        var bytes = Encoding.UTF8.GetBytes(originalText);

        var (_, decoded) = EncodingDetector.DetectAndDecode(bytes);

        Assert.Equal(originalText, decoded);
    }

    [Fact]
    public async Task PlainTextExtractor_ReadsLatin1FileWithoutCorruption()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"encoding-docs-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);

        try
        {
            var filePath = Path.Combine(folder, "latin1.txt");
            var cp1252 = Encoding.GetEncoding(1252);
            var originalText = "Não é possível processar währung café.";
            await File.WriteAllBytesAsync(filePath, cp1252.GetBytes(originalText));

            var extractor = new QuestResume.Core.Extraction.Extractors.PlainTextExtractor();
            var document = await extractor.ExtractAsync(filePath);

            Assert.Equal(originalText, document.Text);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    

    [Fact]
    public void GitIgnoreMatcher_MatchesBasicPatterns()
    {
        var matcher = GitIgnoreMatcher.Parse(new[]
        {
            "# comentário, deve ser ignorado",
            "*.log",
            "**/tmp/*",
            "!important.log",
            "/rootonly.txt"
        });

        Assert.True(matcher.IsIgnored("app.log"));
        Assert.True(matcher.IsIgnored("sub/app.log"));
        Assert.False(matcher.IsIgnored("important.log"));
        Assert.True(matcher.IsIgnored("a/tmp/file.txt"));
        Assert.True(matcher.IsIgnored("tmp/file.txt"));
        Assert.True(matcher.IsIgnored("rootonly.txt"));
        Assert.False(matcher.IsIgnored("sub/rootonly.txt"));
        Assert.False(matcher.IsIgnored("keep.txt"));
    }

    [Fact]
    public async Task IndexFolderAsync_SkipsFilesMatchedByQuestResumeIgnore()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"ignore-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"ignore-index-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, ".questresumeignore"), "*.log\n");
            await File.WriteAllTextAsync(Path.Combine(folder, "keep.txt"), "Conteúdo mantido.");
            await File.WriteAllTextAsync(Path.Combine(folder, "skip.log"), "Conteúdo ignorado.");

            var indexer = new DocumentIndexer();
            var stats = await indexer.IndexFolderAsync(folder, indexPath);

            Assert.Equal(1, stats.FilesProcessed);

            var search = new SearchService(indexPath);
            var files = search.GetIndexedFiles(0, 0);
            Assert.Contains(files, f => f.SourcePath.EndsWith("keep.txt"));
            Assert.DoesNotContain(files, f => f.SourcePath.EndsWith("skip.log"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    

    [Fact]
    public async Task ReindexSingleFileAsync_UpdatesOnlyTargetFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"reindex-single-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"reindex-single-index-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);

        try
        {
            var pathA = Path.Combine(folder, "a.txt");
            var pathB = Path.Combine(folder, "b.txt");
            await File.WriteAllTextAsync(pathA, "Conteúdo original do arquivo A.");
            await File.WriteAllTextAsync(pathB, "Conteúdo do arquivo B, que não deve mudar.");

            var indexer = new DocumentIndexer();
            await indexer.IndexFolderAsync(folder, indexPath);

            
            await File.WriteAllTextAsync(pathA, "Conteúdo ATUALIZADO do arquivo A.");
            var chunkCount = await indexer.ReindexSingleFileAsync(pathA, indexPath);

            Assert.True(chunkCount > 0);

            var search = new SearchService(indexPath);
            var chunksA = search.GetChunksByPath(pathA);
            var chunksB = search.GetChunksByPath(pathB);

            Assert.Contains(chunksA, c => c.ChunkText.Contains("ATUALIZADO"));
            Assert.Single(chunksB);
            Assert.Contains("não deve mudar", chunksB[0].ChunkText);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    

    [Fact]
    public async Task IndexFolderAsync_DetectsMovedFile_ReusesCachedChunks()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"moved-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"moved-index-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);

        try
        {
            var oldPath = Path.Combine(folder, "original.txt");
            const string content = "Conteúdo que será movido para outro arquivo.";
            await File.WriteAllTextAsync(oldPath, content);

            var indexer = new DocumentIndexer();
            await indexer.IndexFolderAsync(folder, indexPath, incrementalIndexingEnabled: true);

            var search = new SearchService(indexPath);
            var originalChunks = search.GetChunksByPath(oldPath);
            Assert.NotEmpty(originalChunks);

            
            File.Delete(oldPath);
            var newPath = Path.Combine(folder, "renamed.txt");
            await File.WriteAllTextAsync(newPath, content);
            
            

            await indexer.IndexFolderAsync(folder, indexPath, incrementalIndexingEnabled: true);

            var newChunks = search.GetChunksByPath(newPath);
            var oldChunksAfterMove = search.GetChunksByPath(oldPath);

            Assert.NotEmpty(newChunks);
            Assert.Equal(originalChunks[0].ChunkText, newChunks[0].ChunkText);
            Assert.Empty(oldChunksAfterMove);

            var manifest = new QuestResume.Core.Persistence.IndexManifestRepository(indexPath).Load();
            Assert.True(manifest.Files.ContainsKey(newPath));
            Assert.False(manifest.Files.ContainsKey(oldPath));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    

    [Fact]
    public async Task CleanOrphansAsync_RemovesDocumentsForDeletedFiles()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"orphan-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"orphan-index-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);

        try
        {
            var keepPath = Path.Combine(folder, "keep.txt");
            var orphanPath = Path.Combine(folder, "willbedeleted.txt");
            await File.WriteAllTextAsync(keepPath, "Este arquivo permanece.");
            await File.WriteAllTextAsync(orphanPath, "Este arquivo será apagado do disco.");

            var indexer = new DocumentIndexer();
            await indexer.IndexFolderAsync(folder, indexPath);

            
            File.Delete(orphanPath);

            var removedCount = await indexer.CleanOrphansAsync(indexPath);
            Assert.Equal(1, removedCount);

            var search = new SearchService(indexPath);
            Assert.Empty(search.GetChunksByPath(orphanPath));
            Assert.NotEmpty(search.GetChunksByPath(keepPath));

            
            var secondRun = await indexer.CleanOrphansAsync(indexPath);
            Assert.Equal(0, secondRun);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }
}
