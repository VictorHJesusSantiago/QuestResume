using QuestResume.Core.Models;

namespace QuestResume.Core.Persistence;

public interface ITagStoreRepository
{
    TagStore Load();
    void Save(TagStore store);
}
