using QuestResume.Core.Models;

namespace QuestResume.Core.Persistence;

public interface ICollectionStore
{
        IReadOnlyList<Collection> List();

        Collection Create(string name);

        bool Delete(string name);

        string ResolvePath(string name);
}
