using QuestResume.Core.Models;

namespace QuestResume.Core.Persistence;

public interface IIndexManifestRepository
{
    IndexManifest Load();
    void Save(IndexManifest manifest);
}
