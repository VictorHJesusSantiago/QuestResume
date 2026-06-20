using QuestResume.Core.Models;

namespace QuestResume.Core.Persistence;

/// <summary>
/// Persists and loads the <see cref="IndexReport"/> JSON sidecar written after each
/// full re-index run.
/// </summary>
public interface IIndexReportRepository
{
    IndexReport Load();
    void Save(IndexReport report);
}
