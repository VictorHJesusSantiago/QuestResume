using QuestResume.Core.Models;

namespace QuestResume.Core.Persistence;

/// <summary>Carrega e salva o sidecar <see cref="SummaryStore"/> (<c>summaries.json</c>), mesmo padrão de <see cref="ITagStoreRepository"/>.</summary>
public interface ISummaryStoreRepository
{
    SummaryStore Load();
    void Save(SummaryStore store);
}
