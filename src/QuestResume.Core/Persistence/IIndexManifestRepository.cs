using QuestResume.Core.Models;

namespace QuestResume.Core.Persistence;

/// <summary>
/// Persists and loads the <see cref="IndexManifest"/> JSON sidecar used for incremental
/// indexing ("skip if unchanged").
/// </summary>
public interface IIndexManifestRepository
{
    IndexManifest Load();
    void Save(IndexManifest manifest);
}
