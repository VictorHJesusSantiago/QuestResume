using QuestResume.Core.Models;

namespace QuestResume.Core.Persistence;

/// <summary>
/// Loads and saves the <see cref="TagStore"/> JSON sidecar. Unlike <see cref="IIndexReportRepository"/>,
/// the tag store must survive a full re-index because tags are user-assigned metadata.
/// </summary>
public interface ITagStoreRepository
{
    TagStore Load();
    void Save(TagStore store);
}
