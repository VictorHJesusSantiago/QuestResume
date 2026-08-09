using QuestResume.Core.Models;

namespace QuestResume.Core.Persistence;

public interface IAuditLogRepository
{
        void Append(AuditLogEntry entry);

        List<AuditLogEntry> Load(int? limit = null);

        void Rotate(int maxLines);
}
