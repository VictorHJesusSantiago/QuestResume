using QuestResume.Core.Configuration;
using QuestResume.Core.Indexing;
using QuestResume.Core.Models;

namespace QuestResume.Core.Services;

/// <summary>
/// Computes aggregated dashboard statistics from the Lucene index, audit log and index report.
/// Extracted from <see cref="DashboardStats"/> to keep the model class a pure data container
/// (Active Record anti-pattern removal).
/// </summary>
public static class DashboardService
{
    public static DashboardStats Compute(string indexPath, AppOptions? options = null, LuceneIndexManager? indexManager = null)
    {
        var search = new SearchService(indexPath, indexManager);
        var indexedFiles = search.GetIndexedFiles();
        var report = IndexReport.Load(indexPath);
        var audit = AuditLog.Load(indexPath);
        var elapsedTimes = audit.Where(a => a.ElapsedMs > 0).Select(a => a.ElapsedMs).ToList();

        return new DashboardStats
        {
            ChunkCount = search.GetDocumentCount(),
            DocumentCount = indexedFiles.Count,
            TagCount = search.GetAllTags().Count,
            ErrorCount = report.Errors.Count,
            DuplicateCount = report.Duplicates.Count,
            QuestionCount = audit.Count,
            LastIndexedUtc = report.GeneratedUtc == default ? null : report.GeneratedUtc,
            IndexSizeBytes = GetDirectorySize(indexPath),
            ProcessMemoryMb = Environment.WorkingSet / 1024.0 / 1024.0,
            AverageResponseTimeMs = elapsedTimes.Count > 0 ? elapsedTimes.Average() : null,
            ModelConfigured = !string.IsNullOrWhiteSpace(options?.ModelPath) && File.Exists(options.ModelPath),
            DocumentsByExtension = indexedFiles
                .GroupBy(GetExtensionLabel)
                .OrderByDescending(g => g.Count())
                .ToDictionary(g => g.Key, g => g.Count()),
            QuestionsByDay = audit
                .GroupBy(a => a.TimestampUtc.ToString("yyyy-MM-dd"))
                .OrderBy(g => g.Key)
                .ToDictionary(g => g.Key, g => g.Count())
        };
    }

    private static string GetExtensionLabel(IndexedFileInfo file)
    {
        var extension = Path.GetExtension(file.FileName);
        return string.IsNullOrEmpty(extension) ? "(sem extensão)" : extension.TrimStart('.').ToLowerInvariant();
    }

    private static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path)) return 0;

        long total = 0;
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            try { total += new FileInfo(file).Length; }
            catch { }
        }

        return total;
    }
}
