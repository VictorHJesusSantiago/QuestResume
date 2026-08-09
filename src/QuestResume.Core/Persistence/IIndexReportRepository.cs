using QuestResume.Core.Models;

namespace QuestResume.Core.Persistence;

public interface IIndexReportRepository
{
    IndexReport Load();
    void Save(IndexReport report);
}
