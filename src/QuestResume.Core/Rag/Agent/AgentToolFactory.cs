using QuestResume.Core.Configuration;
using QuestResume.Core.Indexing;
using QuestResume.Core.Models;

namespace QuestResume.Core.Rag.Agent;

/// <summary>
/// Monta a lista de <see cref="ITool"/> disponíveis para o <see cref="AgentOrchestrator"/> a
/// partir de <see cref="AppOptions"/> (item 11). Centraliza a montagem para que CLI/API/Desktop
/// não dupliquem a lógica de quais ferramentas habilitar. Ferramentas locais (calculadora,
/// data/hora, conversor de unidades, leitor de arquivos, busca no índice, estatísticas do índice)
/// são sempre incluídas quando <see cref="AppOptions.AgentToolsEnabled"/> está ativo; a busca web
/// só é adicionada se <see cref="AppOptions.WebSearchEndpointUrl"/> estiver configurada, por ser a
/// única que faz chamadas de rede externas.
/// </summary>
public static class AgentToolFactory
{
    public static IReadOnlyList<ITool> Build(
        AppOptions options,
        ISearchService? searchService = null,
        Func<DashboardStats>? statsProvider = null,
        HttpClient? httpClient = null)
    {
        var tools = new List<ITool>
        {
            new CalculatorTool(),
            new DateTimeTool(),
            new UnitConverterTool(),
        };

        var allowedRoots = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.DocumentsFolder))
        {
            allowedRoots.Add(options.DocumentsFolder);
        }
        allowedRoots.AddRange(options.AllowedDocumentRoots);
        if (allowedRoots.Count > 0)
        {
            tools.Add(new FileReaderTool(allowedRoots));
        }

        if (searchService is not null)
        {
            tools.Add(new IndexQueryTool(searchService));
        }

        if (statsProvider is not null)
        {
            tools.Add(new IndexStatsTool(statsProvider));
        }

        if (!string.IsNullOrWhiteSpace(options.WebSearchEndpointUrl))
        {
            tools.Add(new WebSearchTool(options.WebSearchEndpointUrl!, httpClient));
        }

        return tools;
    }
}
