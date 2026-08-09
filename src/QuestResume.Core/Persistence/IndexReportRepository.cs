using System.Text.Json;
using QuestResume.Core.Models;

namespace QuestResume.Core.Persistence;

public sealed class IndexReportRepository : IIndexReportRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _indexPath;

    public IndexReportRepository(string indexPath)
    {
        _indexPath = indexPath;
    }

    private string FilePath => Path.Combine(_indexPath, IndexReport.FileName);

    public IndexReport Load()
    {
        if (!File.Exists(FilePath)) return new IndexReport();
        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<IndexReport>(json, SerializerOptions) ?? new IndexReport();
        }
        catch { return new IndexReport(); }
    }

    public void Save(IndexReport report)
    {
        var json = JsonSerializer.Serialize(report, SerializerOptions);
        File.WriteAllText(FilePath, json);
    }
}
