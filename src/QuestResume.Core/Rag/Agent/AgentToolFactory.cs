using QuestResume.Core.Configuration;
using QuestResume.Core.Indexing;
using QuestResume.Core.Models;

namespace QuestResume.Core.Rag.Agent;

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
