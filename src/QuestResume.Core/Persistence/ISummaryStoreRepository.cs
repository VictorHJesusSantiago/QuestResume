using QuestResume.Core.Models;

namespace QuestResume.Core.Persistence;

public interface ISummaryStoreRepository
{
    SummaryStore Load();
    void Save(SummaryStore store);
}
