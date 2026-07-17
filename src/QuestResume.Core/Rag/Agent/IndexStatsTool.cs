using QuestResume.Core.Models;

namespace QuestResume.Core.Rag.Agent;

/// <summary>
/// Retorna estatísticas do índice (item 11) a partir de <see cref="DashboardStats"/>: número de
/// documentos, trechos, tags, perguntas, tamanho do índice etc. Útil para o agente responder
/// perguntas do tipo "quantos documentos estão indexados?". A fonte dos dados é injetada
/// (<see cref="Func{DashboardStats}"/>) para não acoplar a ferramenta ao cálculo em disco e
/// permitir testes.
/// </summary>
public sealed class IndexStatsTool : ITool
{
    private readonly Func<DashboardStats> _statsProvider;

    public IndexStatsTool(Func<DashboardStats> statsProvider)
    {
        _statsProvider = statsProvider;
    }

    public string Name => "index_stats";

    public string Description =>
        "Retorna estatísticas do índice atual: quantidade de documentos, trechos, tags, perguntas " +
        "feitas e tamanho do índice. Use para perguntas sobre o estado/estatísticas da base indexada.";

    public Task<string> InvokeAsync(string input, CancellationToken cancellationToken = default)
    {
        var s = _statsProvider();
        var lastIndexed = s.LastIndexedUtc?.ToString("dd/MM/yyyy HH:mm 'UTC'") ?? "nunca";
        var sizeMb = Math.Round(s.IndexSizeBytes / (1024d * 1024d), 2);

        var text =
            $"Documentos indexados: {s.DocumentCount}\n" +
            $"Trechos (chunks): {s.ChunkCount}\n" +
            $"Tags: {s.TagCount}\n" +
            $"Erros de indexação: {s.ErrorCount}\n" +
            $"Duplicatas: {s.DuplicateCount}\n" +
            $"Perguntas feitas: {s.QuestionCount}\n" +
            $"Tamanho do índice: {sizeMb} MB\n" +
            $"Última indexação: {lastIndexed}\n" +
            $"Modelo configurado: {(s.ModelConfigured ? "sim" : "não")}";

        return Task.FromResult(text);
    }
}
