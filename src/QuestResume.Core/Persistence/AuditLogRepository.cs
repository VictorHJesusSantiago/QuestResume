using System.Collections.Concurrent;
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

    // One semaphore per normalized index path so two AuditLogRepository instances pointed at
    // the same file — e.g. RagQueryEngine's per-call Append/Rotate and
    // AuditLogRotationService's background Rotate — never race on the same file.
    // Without this, an interleaved ReadAllLines/WriteAllLines (Rotate) over an in-progress
    // AppendAllText (Append) can silently lose entries or produce a corrupt JSONL line.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new();

    private readonly string _indexPath;

    public AuditLogRepository(string indexPath)
    {
        _indexPath = indexPath;
    }

    private string FilePath => Path.Combine(_indexPath, AuditLog.FileName);

    private SemaphoreSlim FileLock =>
        _fileLocks.GetOrAdd(Path.GetFullPath(_indexPath), _ => new SemaphoreSlim(1, 1));

    public void Append(AuditLogEntry entry)
    {
        var sem = FileLock;
        sem.Wait();
        try
        {
            var line = JsonSerializer.Serialize(entry, SerializerOptions);
            File.AppendAllText(FilePath, line + Environment.NewLine);
        }
        catch { }
        finally { sem.Release(); }
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
        var sem = FileLock;
        sem.Wait();
        try
        {
            if (!File.Exists(FilePath)) return;
            // Fast guard: each audit JSONL line is at least ~50 bytes. When the file is
            // clearly under the limit we avoid the expensive ReadAllLines entirely, making
            // per-call rotation from RagQueryEngine's hot-path nearly free.
            if (new FileInfo(FilePath).Length < (long)maxLines * 50) return;
            var lines = File.ReadAllLines(FilePath);
            if (lines.Length <= maxLines) return;
            File.WriteAllLines(FilePath, lines.Skip(lines.Length - maxLines));
        }
        catch { }
        finally { sem.Release(); }
    }
}
