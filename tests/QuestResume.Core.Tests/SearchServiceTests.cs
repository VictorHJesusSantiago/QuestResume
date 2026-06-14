using QuestResume.Core.Indexing;

namespace QuestResume.Core.Tests;

public class SearchServiceTests
{
    [Fact]
    public async Task Search_ReturnsHighlightWithMatchedTerm()
    {
        var (folder, indexPath) = await CreateIndexedFolderAsync();

        try
        {
            var search = new SearchService(indexPath);
            var results = search.Search("QuestResume", topK: 5);

            Assert.NotEmpty(results);
            var highlighted = Assert.Single(results, r => r.Highlight is not null);
            Assert.NotEqual(highlighted.ChunkText, highlighted.Highlight);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task Search_WithExtensionFilter_OnlyReturnsMatchingExtension()
    {
        var (folder, indexPath) = await CreateIndexedFolderAsync();

        try
        {
            var search = new SearchService(indexPath);

            var matching = search.Search("documentos", topK: 5, new SearchFilters(Extension: ".txt"));
            Assert.NotEmpty(matching);
            Assert.All(matching, r => Assert.EndsWith(".txt", r.FileName, StringComparison.OrdinalIgnoreCase));

            var nonMatching = search.Search("documentos", topK: 5, new SearchFilters(Extension: ".pdf"));
            Assert.Empty(nonMatching);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task Search_WithFolderFilter_OnlyReturnsFilesUnderFolder()
    {
        var (folder, indexPath) = await CreateIndexedFolderAsync();

        try
        {
            var search = new SearchService(indexPath);

            var matching = search.Search("documentos", topK: 5, new SearchFilters(FolderPath: folder));
            Assert.NotEmpty(matching);

            var nonMatching = search.Search("documentos", topK: 5, new SearchFilters(FolderPath: Path.Combine(folder, "outra-pasta")));
            Assert.Empty(nonMatching);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task GetChunksByPath_ReturnsAllChunksOrderedByIndex()
    {
        var (folder, indexPath) = await CreateIndexedFolderAsync();

        try
        {
            var search = new SearchService(indexPath);
            var filePath = Path.Combine(folder, "doc.txt");

            var chunks = search.GetChunksByPath(filePath);

            Assert.NotEmpty(chunks);
            Assert.All(chunks, c => Assert.Equal(filePath, c.SourcePath));
            Assert.Equal(chunks.Select(c => c.ChunkIndex).OrderBy(i => i), chunks.Select(c => c.ChunkIndex));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task GetChunksByPath_UnknownPath_ReturnsEmpty()
    {
        var (folder, indexPath) = await CreateIndexedFolderAsync();

        try
        {
            var search = new SearchService(indexPath);
            var chunks = search.GetChunksByPath(Path.Combine(folder, "nao-existe.txt"));

            Assert.Empty(chunks);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task GetIndexedFiles_ReturnsFileWithChunkCount()
    {
        var (folder, indexPath) = await CreateIndexedFolderAsync();

        try
        {
            var search = new SearchService(indexPath);
            var files = search.GetIndexedFiles();

            var filePath = Path.Combine(folder, "doc.txt");
            var file = Assert.Single(files);
            Assert.Equal(filePath, file.SourcePath);
            Assert.Equal("doc.txt", file.FileName);
            Assert.True(file.ChunkCount > 0);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task RemoveDocument_RemovesAllChunksForPath()
    {
        var (folder, indexPath) = await CreateIndexedFolderAsync();

        try
        {
            var search = new SearchService(indexPath);
            var filePath = Path.Combine(folder, "doc.txt");

            var removed = search.RemoveDocument(filePath);

            Assert.True(removed > 0);
            Assert.Empty(search.GetChunksByPath(filePath));
            Assert.Empty(search.GetIndexedFiles());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task RemoveDocument_UnknownPath_ReturnsZero()
    {
        var (folder, indexPath) = await CreateIndexedFolderAsync();

        try
        {
            var search = new SearchService(indexPath);
            var removed = search.RemoveDocument(Path.Combine(folder, "nao-existe.txt"));

            Assert.Equal(0, removed);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    private static async Task<(string Folder, string IndexPath)> CreateIndexedFolderAsync()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"search-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"search-index-{Guid.NewGuid()}");

        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(
            Path.Combine(folder, "doc.txt"),
            "QuestResume é um sistema de perguntas e respostas offline sobre documentos locais.");

        var indexer = new DocumentIndexer();
        await indexer.IndexFolderAsync(folder, indexPath);

        return (folder, indexPath);
    }
}
