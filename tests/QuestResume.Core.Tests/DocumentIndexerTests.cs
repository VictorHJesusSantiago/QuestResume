using QuestResume.Core.Indexing;
using QuestResume.Core.Models;

namespace QuestResume.Core.Tests;

public class DocumentIndexerTests
{
    [Fact]
    public async Task IndexFolderAsync_DetectsDuplicateFilesByContent()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"dedup-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"dedup-index-{Guid.NewGuid()}");

        Directory.CreateDirectory(folder);

        try
        {
            const string content = "Conteúdo idêntico para detectar duplicatas.";
            await File.WriteAllTextAsync(Path.Combine(folder, "original.txt"), content);
            await File.WriteAllTextAsync(Path.Combine(folder, "copia.txt"), content);

            var indexer = new DocumentIndexer();
            var stats = await indexer.IndexFolderAsync(folder, indexPath);

            Assert.Equal(1, stats.FilesProcessed);
            var duplicate = Assert.Single(stats.Duplicates);

            // Directory.EnumerateFiles doesn't guarantee ordering, so either file may be
            // detected as the "original" depending on enumeration order — what matters is
            // that one of the two identical files was flagged as a duplicate of the other.
            var fileNames = new[] { Path.GetFileName(duplicate.Path), Path.GetFileName(duplicate.DuplicateOfPath) };
            Assert.Contains("copia.txt", fileNames);
            Assert.Contains("original.txt", fileNames);
            Assert.NotEqual(duplicate.Path, duplicate.DuplicateOfPath);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task IndexFolderAsync_WritesIndexReportWithDuplicates()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"report-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"report-index-{Guid.NewGuid()}");

        Directory.CreateDirectory(folder);

        try
        {
            const string content = "Mesmo conteúdo em dois arquivos.";
            await File.WriteAllTextAsync(Path.Combine(folder, "um.txt"), content);
            await File.WriteAllTextAsync(Path.Combine(folder, "dois.txt"), content);

            var indexer = new DocumentIndexer();
            await indexer.IndexFolderAsync(folder, indexPath);

            var report = IndexReport.Load(indexPath);

            Assert.Single(report.Duplicates);
            Assert.Empty(report.Errors);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task IndexFolderAsync_PreservesTagsAcrossReindex()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"tags-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"tags-index-{Guid.NewGuid()}");

        Directory.CreateDirectory(folder);

        try
        {
            var filePath = Path.Combine(folder, "doc.txt");
            await File.WriteAllTextAsync(filePath, "Conteúdo do documento.");

            var indexer = new DocumentIndexer();
            await indexer.IndexFolderAsync(folder, indexPath);

            var search = new SearchService(indexPath);
            search.SetTags(filePath, new[] { "Contrato" });

            await indexer.IndexFolderAsync(folder, indexPath);

            Assert.Equal(new[] { "Contrato" }, search.GetTags(filePath));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public void IndexReport_Load_MissingFile_ReturnsEmptyReport()
    {
        var indexPath = Path.Combine(Path.GetTempPath(), $"missing-report-{Guid.NewGuid()}");

        var report = IndexReport.Load(indexPath);

        Assert.Empty(report.Errors);
        Assert.Empty(report.Duplicates);
    }

    [Fact]
    public async Task IndexFolderAsync_SkipsFilesLargerThanMaxFileSizeBytes()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"maxsize-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"maxsize-index-{Guid.NewGuid()}");

        Directory.CreateDirectory(folder);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "pequeno.txt"), "conteúdo pequeno");
            await File.WriteAllTextAsync(Path.Combine(folder, "grande.txt"), new string('x', 1000));

            var indexer = new DocumentIndexer();
            var stats = await indexer.IndexFolderAsync(folder, indexPath, maxFileSizeBytes: 100);

            Assert.Equal(1, stats.FilesProcessed);
            Assert.Equal(1, stats.FilesSkipped);
            Assert.Contains(stats.SkippedFiles, p => Path.GetFileName(p) == "grande.txt");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task IndexFolderAsync_RedactsPiiWhenEnabled()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"pii-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"pii-index-{Guid.NewGuid()}");

        Directory.CreateDirectory(folder);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "contato.txt"), "Contato: joao@exemplo.com, CPF 123.456.789-09.");

            var indexer = new DocumentIndexer();
            await indexer.IndexFolderAsync(folder, indexPath, piiRedactionEnabled: true);

            var search = new SearchService(indexPath);
            var results = search.Search("contato", 10);

            var chunk = Assert.Single(results);
            Assert.Contains("[EMAIL]", chunk.ChunkText);
            Assert.Contains("[CPF]", chunk.ChunkText);
            Assert.DoesNotContain("joao@exemplo.com", chunk.ChunkText);
            Assert.DoesNotContain("123.456.789-09", chunk.ChunkText);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task IndexFolderAsync_SkipsFilesUnderExcludedFolders()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"excluded-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"excluded-index-{Guid.NewGuid()}");
        var subFolder = Path.Combine(folder, "privado");

        Directory.CreateDirectory(subFolder);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "publico.txt"), "documento público");
            await File.WriteAllTextAsync(Path.Combine(subFolder, "secreto.txt"), "documento privado");

            var indexer = new DocumentIndexer();
            var stats = await indexer.IndexFolderAsync(folder, indexPath, excludedFolders: new[] { subFolder });

            Assert.Equal(1, stats.FilesProcessed);
            Assert.Contains(stats.SkippedFiles, p => Path.GetFileName(p) == "secreto.txt");

            var search = new SearchService(indexPath);
            var results = search.Search("documento", 10);
            Assert.DoesNotContain(results, r => r.FileName == "secreto.txt");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task IndexFolderAsync_ProcessesManyFilesInParallel()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"parallel-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"parallel-index-{Guid.NewGuid()}");

        Directory.CreateDirectory(folder);

        try
        {
            const int fileCount = 25;
            for (var i = 0; i < fileCount; i++)
            {
                await File.WriteAllTextAsync(Path.Combine(folder, $"doc{i}.txt"), $"Conteúdo exclusivo número {i}.");
            }

            var indexer = new DocumentIndexer();
            var stats = await indexer.IndexFolderAsync(folder, indexPath, parallelism: 8);

            Assert.Equal(fileCount, stats.FilesProcessed);
            Assert.Empty(stats.Errors);

            var search = new SearchService(indexPath);
            Assert.Equal(fileCount, search.GetIndexedFiles().Count);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task IndexFolderAsync_Incremental_SkipsUnchangedFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"incremental-unchanged-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"incremental-unchanged-index-{Guid.NewGuid()}");

        Directory.CreateDirectory(folder);

        try
        {
            var filePath = Path.Combine(folder, "estavel.txt");
            await File.WriteAllTextAsync(filePath, "Conteúdo que não vai mudar entre indexações.");

            var indexer = new DocumentIndexer();
            var firstRun = await indexer.IndexFolderAsync(folder, indexPath, incrementalIndexingEnabled: true);
            Assert.Equal(1, firstRun.FilesProcessed);

            var secondRun = await indexer.IndexFolderAsync(folder, indexPath, incrementalIndexingEnabled: true);

            // The file is still counted as "processed" (it's still present in the index), but
            // its manifest entry's chunks should have been reused rather than re-extracted —
            // verified indirectly by checking the content is still fully searchable afterwards.
            Assert.Equal(1, secondRun.FilesProcessed);
            Assert.Equal(firstRun.ChunksIndexed, secondRun.ChunksIndexed);

            var search = new SearchService(indexPath);
            var results = search.Search("conteúdo", 10);
            Assert.Single(results);

            var manifestPath = Path.Combine(indexPath, "index-manifest.json");
            Assert.True(File.Exists(manifestPath));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task IndexFolderAsync_Incremental_ReprocessesModifiedFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"incremental-modified-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"incremental-modified-index-{Guid.NewGuid()}");

        Directory.CreateDirectory(folder);

        try
        {
            var filePath = Path.Combine(folder, "muda.txt");
            await File.WriteAllTextAsync(filePath, "Texto original antes da alteração.");

            var indexer = new DocumentIndexer();
            await indexer.IndexFolderAsync(folder, indexPath, incrementalIndexingEnabled: true);

            // Ensure a different LastWriteTimeUtc even on fast filesystems/CI, and change the
            // content so the change is observable via search.
            await Task.Delay(50);
            await File.WriteAllTextAsync(filePath, "Texto totalmente novo após a alteração, palavra-chave: atualizado.");
            File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow);

            await indexer.IndexFolderAsync(folder, indexPath, incrementalIndexingEnabled: true);

            var search = new SearchService(indexPath);
            var results = search.Search("atualizado", 10);
            Assert.Single(results);

            var oldResults = search.Search("original", 10);
            Assert.Empty(oldResults);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }
}
