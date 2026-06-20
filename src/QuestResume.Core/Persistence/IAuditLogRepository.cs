using QuestResume.Core.Models;

namespace QuestResume.Core.Persistence;

/// <summary>
/// Append-only persistence for <see cref="AuditLogEntry"/> records.
/// Abstracts the JSONL file backend so callers are not coupled to file I/O.
/// </summary>
public interface IAuditLogRepository
{
    /// <summary>Appends <paramref name="entry"/> to the log. Best-effort: never throws.</summary>
    void Append(AuditLogEntry entry);

    /// <summary>Loads entries most-recent-first, optionally limited to <paramref name="limit"/> entries.</summary>
    List<AuditLogEntry> Load(int? limit = null);

    /// <summary>
    /// Trims the log to the <paramref name="maxLines"/> most-recent entries.
    /// No-op when <paramref name="maxLines"/> is zero (unlimited).
    /// </summary>
    void Rotate(int maxLines);
}
