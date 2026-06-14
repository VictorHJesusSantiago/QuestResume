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
}
