using System.Collections.Concurrent;
using System.Text.Json;
using QuestResume.Core.Models;

namespace QuestResume.Core.Persistence;

public sealed class AuditLogRepository : IAuditLogRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new();

    
    
    
    
    
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
            
            
            
            if (new FileInfo(FilePath).Length < (long)maxLines * 50) return;
            var lines = File.ReadAllLines(FilePath);
            if (lines.Length <= maxLines) return;
            File.WriteAllLines(FilePath, lines.Skip(lines.Length - maxLines));
        }
        catch { }
        finally { sem.Release(); }
    }
}
