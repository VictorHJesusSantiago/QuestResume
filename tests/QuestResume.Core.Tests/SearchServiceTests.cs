using QuestResume.Core.Embeddings;
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
            // The highlighter must produce a non-null result for the matched term; whether
            // the fragment is shorter than ChunkText depends on document length vs. the
            // SimpleFragmenter window, so we only assert non-null here.
            Assert.Contains(results, r => r.Highlight is not null);
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
    public async Task SetTags_ThenGetTags_RoundTrips()
    {
        var (folder, indexPath) = await CreateIndexedFolderAsync();

        try
        {
            var search = new SearchService(indexPath);
            var filePath = Path.Combine(folder, "doc.txt");

            search.SetTags(filePath, new[] { "Contrato", "Urgente" });

            Assert.Equal(new[] { "Contrato", "Urgente" }, search.GetTags(filePath));
            Assert.Equal(new[] { "Contrato", "Urgente" }, search.GetAllTags());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task GetIndexedFiles_IncludesAssignedTags()
    {
        var (folder, indexPath) = await CreateIndexedFolderAsync();

        try
        {
            var search = new SearchService(indexPath);
            var filePath = Path.Combine(folder, "doc.txt");

            search.SetTags(filePath, new[] { "Contrato" });

            var file = Assert.Single(search.GetIndexedFiles());
            Assert.Equal(new[] { "Contrato" }, file.Tags);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task Search_WithTagFilter_OnlyReturnsTaggedFiles()
    {
        var (folder, indexPath) = await CreateIndexedFolderAsync();

        try
        {
            var search = new SearchService(indexPath);
            var filePath = Path.Combine(folder, "doc.txt");

            var matching = search.Search("documentos", topK: 5, new SearchFilters(Tag: "Contrato"));
            Assert.Empty(matching);

            search.SetTags(filePath, new[] { "Contrato" });

            matching = search.Search("documentos", topK: 5, new SearchFilters(Tag: "contrato"));
            Assert.NotEmpty(matching);
            Assert.All(matching, r => Assert.Equal(filePath, r.SourcePath));

            var nonMatching = search.Search("documentos", topK: 5, new SearchFilters(Tag: "Outra"));
            Assert.Empty(nonMatching);
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

    [Fact]
    public async Task Search_UsesBrazilianStemming_ContratosMatchesContrato()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"stem-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"stem-index-{Guid.NewGuid()}");

        Directory.CreateDirectory(folder);

        try
        {
            // "contrato" (singular) is indexed; a plural-form query "contratos" should still
            // match because BrazilianAnalyzer stems both to the same root — StandardAnalyzer
            // would not do this.
            await File.WriteAllTextAsync(
                Path.Combine(folder, "contrato.txt"),
                "Este documento é um contrato de prestação de serviços.");

            var indexer = new DocumentIndexer();
            await indexer.IndexFolderAsync(folder, indexPath);

            var search = new SearchService(indexPath);
            var results = search.Search("contratos", topK: 5);

            Assert.NotEmpty(results);
            Assert.Contains(results, r => r.FileName == "contrato.txt");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task Search_WithFuzzy_MatchesMisspelledTerm()
    {
        var (folder, indexPath) = await CreateIndexedFolderAsync();

        try
        {
            var search = new SearchService(indexPath);

            // "offlinee" (typo, extra letter) shouldn't match "offline" without fuzzy...
            var exact = search.Search("offlinee", topK: 5);
            Assert.Empty(exact);

            // ...but should match with Fuzzy enabled, tolerant to the extra letter.
            var fuzzy = search.Search("offlinee", topK: 5, new SearchFilters(Fuzzy: true));
            Assert.NotEmpty(fuzzy);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task Search_WithQuotedPhrase_MatchesExactPhrase()
    {
        var (folder, indexPath) = await CreateIndexedFolderAsync();

        try
        {
            var search = new SearchService(indexPath);

            var results = search.Search("\"sistema de perguntas\"", topK: 5);
            Assert.NotEmpty(results);

            // Reversed word order shouldn't match the exact phrase.
            var noMatch = search.Search("\"perguntas de sistema\"", topK: 5);
            Assert.Empty(noMatch);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task Search_WithBooleanNot_ExcludesTerm()
    {
        var (folder, indexPath) = await CreateIndexedFolderAsync();

        try
        {
            var search = new SearchService(indexPath);

            var results = search.Search("documentos NOT locais", topK: 5);
            Assert.Empty(results);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task Search_WithInvalidSyntax_ThrowsSearchQuerySyntaxException()
    {
        var (folder, indexPath) = await CreateIndexedFolderAsync();

        try
        {
            var search = new SearchService(indexPath);

            Assert.Throws<SearchQuerySyntaxException>(() => search.Search("\"unbalanced quote", topK: 5));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task Search_WithSizeFilter_OnlyReturnsMatchingSize()
    {
        var (folder, indexPath) = await CreateIndexedFolderAsync();

        try
        {
            var search = new SearchService(indexPath);
            var fileSize = new FileInfo(Path.Combine(folder, "doc.txt")).Length;

            var matching = search.Search("documentos", topK: 5, new SearchFilters(MinSizeBytes: fileSize, MaxSizeBytes: fileSize));
            Assert.NotEmpty(matching);

            var nonMatching = search.Search("documentos", topK: 5, new SearchFilters(MinSizeBytes: fileSize + 1));
            Assert.Empty(nonMatching);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task Search_WithDateFilter_OnlyReturnsMatchingRange()
    {
        var (folder, indexPath) = await CreateIndexedFolderAsync();

        try
        {
            var search = new SearchService(indexPath);

            var future = DateTime.UtcNow.AddDays(1);
            var nonMatching = search.Search("documentos", topK: 5, new SearchFilters(DateFrom: future));
            Assert.Empty(nonMatching);

            var past = DateTime.UtcNow.AddDays(-1);
            var matching = search.Search("documentos", topK: 5, new SearchFilters(DateFrom: past));
            Assert.NotEmpty(matching);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task Search_WithSortByName_OrdersResultsAlphabetically()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"sort-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"sort-index-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "b-doc.txt"), "documentos de teste sobre gatos, arquivo b.");
            await File.WriteAllTextAsync(Path.Combine(folder, "a-doc.txt"), "documentos de teste sobre gatos, arquivo a.");

            var indexer = new DocumentIndexer();
            await indexer.IndexFolderAsync(folder, indexPath);

            var search = new SearchService(indexPath);
            var ascending = search.Search("gatos", topK: 5, new SearchFilters(SortBy: "name", SortDescending: false));

            Assert.True(ascending.Count >= 2);
            Assert.Equal("a-doc.txt", ascending[0].FileName);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task Search_WithPagination_SplitsResultsAcrossPages()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"page-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"page-index-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);

        try
        {
            for (var i = 0; i < 5; i++)
            {
                await File.WriteAllTextAsync(Path.Combine(folder, $"doc{i}.txt"), $"paginacao termo de busca unico, arquivo numero {i}.");
            }

            var indexer = new DocumentIndexer();
            await indexer.IndexFolderAsync(folder, indexPath);

            var search = new SearchService(indexPath);
            var page1 = search.Search("paginacao", topK: 100, new SearchFilters(SortBy: "name", SortDescending: false, Page: 1, PageSize: 2));
            var page2 = search.Search("paginacao", topK: 100, new SearchFilters(SortBy: "name", SortDescending: false, Page: 2, PageSize: 2));

            Assert.Equal(2, page1.Count);
            Assert.Equal(2, page2.Count);
            Assert.NotEqual(page1[0].FileName, page2[0].FileName);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task ExpandSentenceWindow_ReturnsSurroundingSentences()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"window-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"window-index-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(folder, "doc.txt"),
                "Frase um sobre gatos. Frase dois sobre cachorros. Frase tres sobre unicornios raros. Frase quatro sobre passaros. Frase cinco sobre peixes.");

            var indexer = new DocumentIndexer();
            await indexer.IndexFolderAsync(folder, indexPath, chunkSize: 1000, overlap: 0, sentenceWindowChunkingEnabled: true);

            var search = new SearchService(indexPath);
            var results = search.Search("unicornios", topK: 5);
            Assert.NotEmpty(results);
            Assert.DoesNotContain("gatos", results[0].ChunkText);

            var expanded = search.ExpandSentenceWindow(results, windowSize: 1);

            Assert.Contains("cachorros", expanded[0].ChunkText);
            Assert.Contains("unicornios", expanded[0].ChunkText);
            Assert.Contains("passaros", expanded[0].ChunkText);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task SuggestSpelling_MisspelledTerm_ReturnsClosestIndexedTerm()
    {
        var (folder, indexPath) = await CreateIndexedFolderAsync();

        try
        {
            var search = new SearchService(indexPath);

            // The indexed term is "document" (BrazilianAnalyzer stems "documentos" down to its
            // root); "documenty" is a 1-edit typo of it (t -> y).
            var suggestions = search.SuggestSpelling("documenty");

            Assert.NotEmpty(suggestions);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task SuggestSpelling_NoIndex_ReturnsEmpty()
    {
        var indexPath = Path.Combine(Path.GetTempPath(), $"noindex-{Guid.NewGuid()}");
        var search = new SearchService(indexPath);

        var suggestions = search.SuggestSpelling("qualquer");

        Assert.Empty(suggestions);
    }

    [Fact]
    public async Task Suggest_ReturnsTermsStartingWithPrefix()
    {
        var (folder, indexPath) = await CreateIndexedFolderAsync();

        try
        {
            var search = new SearchService(indexPath);

            // "documentos" is stemmed by BrazilianAnalyzer; whatever the stored term prefix is,
            // it should start with "docu".
            var suggestions = search.Suggest("docu");

            Assert.NotEmpty(suggestions);
            Assert.All(suggestions, s => Assert.StartsWith("docu", s, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task Suggest_NoMatchingPrefix_ReturnsEmpty()
    {
        var (folder, indexPath) = await CreateIndexedFolderAsync();

        try
        {
            var search = new SearchService(indexPath);

            var suggestions = search.Suggest("zzzznaoexiste");

            Assert.Empty(suggestions);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task FindSimilarAsync_WithoutVectorStore_ThrowsEmbeddingsNotConfigured()
    {
        var (folder, indexPath) = await CreateIndexedFolderAsync();

        try
        {
            var search = new SearchService(indexPath);

            await Assert.ThrowsAsync<QuestResume.Core.Embeddings.EmbeddingsNotConfiguredException>(
                () => search.FindSimilarAsync(Path.Combine(folder, "doc.txt"), topK: 5));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task FindSimilarAsync_ReturnsMostSimilarDocumentExcludingReference()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"similar-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"similar-index-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);

        await File.WriteAllTextAsync(Path.Combine(folder, "gatos-a.txt"), "O gato dorme muito.");
        await File.WriteAllTextAsync(Path.Combine(folder, "gatos-b.txt"), "O gato caça ratos o dia todo.");
        await File.WriteAllTextAsync(Path.Combine(folder, "carros.txt"), "O carro precisa de gasolina para andar.");

        try
        {
            var embeddingService = new FakeEmbeddingService(new[] { "gato", "carro" });
            using var vectorStore = new VectorStore(indexPath);
            var indexer = new DocumentIndexer(embeddingService: embeddingService, vectorStore: vectorStore);
            await indexer.IndexFolderAsync(folder, indexPath);

            var search = new SearchService(indexPath, indexManager: null, vectorStore, clipService: null);
            var results = await search.FindSimilarAsync(Path.Combine(folder, "gatos-a.txt"), topK: 5);

            Assert.NotEmpty(results);
            Assert.Equal("gatos-b.txt", results[0].FileName);
            Assert.DoesNotContain(results, r => r.FileName == "gatos-a.txt");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task ClusterDocumentsAsync_WithoutVectorStore_ThrowsEmbeddingsNotConfigured()
    {
        var (folder, indexPath) = await CreateIndexedFolderAsync();

        try
        {
            var search = new SearchService(indexPath);

            await Assert.ThrowsAsync<QuestResume.Core.Embeddings.EmbeddingsNotConfiguredException>(
                () => search.ClusterDocumentsAsync());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task ClusterDocumentsAsync_GroupsDocumentsByTopic()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"cluster-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"cluster-index-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);

        await File.WriteAllTextAsync(Path.Combine(folder, "gatos-a.txt"), "O gato dorme muito o dia todo.");
        await File.WriteAllTextAsync(Path.Combine(folder, "gatos-b.txt"), "O gato caça ratos o dia todo.");
        await File.WriteAllTextAsync(Path.Combine(folder, "carros-a.txt"), "O carro precisa de gasolina para andar.");
        await File.WriteAllTextAsync(Path.Combine(folder, "carros-b.txt"), "O carro novo tem motor potente.");

        try
        {
            var embeddingService = new FakeEmbeddingService(new[] { "gato", "carro" });
            using var vectorStore = new VectorStore(indexPath);
            var indexer = new DocumentIndexer(embeddingService: embeddingService, vectorStore: vectorStore);
            await indexer.IndexFolderAsync(folder, indexPath);

            var search = new SearchService(indexPath, indexManager: null, vectorStore, clipService: null);
            var clusters = await search.ClusterDocumentsAsync(k: 2);

            Assert.Equal(2, clusters.Count);
            Assert.Equal(4, clusters.Sum(c => c.SourcePaths.Count));

            var catCluster = clusters.First(c => c.SourcePaths.Any(p => p.Contains("gatos-a")));
            Assert.Contains(catCluster.SourcePaths, p => p.Contains("gatos-b"));

            var carCluster = clusters.First(c => c.SourcePaths.Any(p => p.Contains("carros-a")));
            Assert.Contains(carCluster.SourcePaths, p => p.Contains("carros-b"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task ClusterDocumentsAsync_WithLlmProvider_GeneratesLabels()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"cluster-label-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"cluster-label-index-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);

        await File.WriteAllTextAsync(Path.Combine(folder, "a.txt"), "O gato dorme muito o dia todo.");
        await File.WriteAllTextAsync(Path.Combine(folder, "b.txt"), "O gato caça ratos o dia todo.");

        try
        {
            var embeddingService = new FakeEmbeddingService(new[] { "gato" });
            using var vectorStore = new VectorStore(indexPath);
            var indexer = new DocumentIndexer(embeddingService: embeddingService, vectorStore: vectorStore);
            await indexer.IndexFolderAsync(folder, indexPath);

            var search = new SearchService(indexPath, indexManager: null, vectorStore, clipService: null);
            var llm = new FakeLlmProvider(_ => "Gatos");
            var clusters = await search.ClusterDocumentsAsync(k: 1, llmProvider: llm);

            Assert.Single(clusters);
            Assert.Equal("Gatos", clusters[0].Label);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task Search_WithLanguageFilter_OnlyReturnsMatchingLanguage()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"lang-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"lang-index-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);

        await File.WriteAllTextAsync(Path.Combine(folder, "pt.txt"),
            "Este documento não é apenas para você, mas também para todos que estão aqui, porque QuestResume ajuda muito.");
        await File.WriteAllTextAsync(Path.Combine(folder, "en.txt"),
            "This document is not just for you but also for everyone who will need it, because QuestResume helps a lot with their questions.");

        try
        {
            var indexer = new DocumentIndexer();
            await indexer.IndexFolderAsync(folder, indexPath);

            var search = new SearchService(indexPath);

            var ptResults = search.Search("QuestResume", topK: 5, new SearchFilters(Language: "pt"));
            Assert.NotEmpty(ptResults);
            Assert.All(ptResults, r => Assert.Equal("pt.txt", r.FileName));

            var enResults = search.Search("QuestResume", topK: 5, new SearchFilters(Language: "en"));
            Assert.NotEmpty(enResults);
            Assert.All(enResults, r => Assert.Equal("en.txt", r.FileName));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task GetIndexedFiles_PopulatesLanguage()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"lang-files-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"lang-files-index-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);

        await File.WriteAllTextAsync(Path.Combine(folder, "pt.txt"),
            "Este documento não é apenas para você, mas também para todos que estão aqui, porque QuestResume ajuda muito.");

        try
        {
            var indexer = new DocumentIndexer();
            await indexer.IndexFolderAsync(folder, indexPath);

            var search = new SearchService(indexPath);
            var files = search.GetIndexedFiles();

            Assert.Single(files);
            Assert.Equal("pt", files[0].Language);
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
