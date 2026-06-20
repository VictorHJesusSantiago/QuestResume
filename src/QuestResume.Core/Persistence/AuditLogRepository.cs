using System.Text.Json;
using QuestResume.Core.Models;

namespace QuestResume.Core.Persistence;

/// <summary>
/// File-backed implementation of <see cref="IAuditLogRepository"/> using a JSONL sidecar
/// alongside the Lucene index. All I/O is best-effort (exceptions swallowed) so auditing
/// never breaks the <c>AskAsync</c> caller path.
/// </summary>
public sealed class AuditLogRepository : IAuditLogRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new();

    private readonly string _indexPath;

    public AuditLogRepository(string indexPath)
    {
        _indexPath = indexPath;
    }

    private string FilePath => Path.Combine(_indexPath, AuditLog.FileName);

    public void Append(AuditLogEntry entry)
    {
        try
        {
            var line = JsonSerializer.Serialize(entry, SerializerOptions);
            File.AppendAllText(FilePath, line + Environment.NewLine);
        }
        catch { }
    }

    public List<AuditLogEntry> Load(int? limit = null)
    {
        if (!File.Exists(FilePath)) return new List<AuditLogEntry>();

        var entries = new List<AuditLogEntry>();
        foreach (var line in File.ReadAllLines(FilePath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var entry = JsonSerializer.Deserialize<AuditLogEntry>(line, SerializerOptions);
                if (entry is not null) entries.Add(entry);
            }
            catch { }
        }

        entries.Reverse();
        return limit is > 0 ? entries.Take(limit.Value).ToList() : entries;
    }

    public void Rotate(int maxLines)
    {
        if (maxLines <= 0) return;
        try
        {
            if (!File.Exists(FilePath)) return;
            var lines = File.ReadAllLines(FilePath);
            if (lines.Length <= maxLines) return;
            File.WriteAllLines(FilePath, lines.Skip(lines.Length - maxLines));
        }
        catch { }
    }
}
