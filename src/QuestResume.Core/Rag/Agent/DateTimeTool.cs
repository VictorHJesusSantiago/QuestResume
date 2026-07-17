using System.Globalization;

namespace QuestResume.Core.Rag.Agent;

/// <summary>
/// Ferramenta que retorna a data e hora atuais (item 11). Aceita opcionalmente "utc" na entrada
/// para devolver o horário UTC; caso contrário devolve o horário local do sistema.
/// </summary>
public sealed class DateTimeTool : ITool
{
    private readonly Func<DateTimeOffset> _now;

    /// <param name="now">Fonte de tempo injetável (útil para testes determinísticos).</param>
    public DateTimeTool(Func<DateTimeOffset>? now = null)
    {
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public string Name => "datetime";

    public string Description =>
        "Retorna a data e a hora atuais. Use para perguntas sobre 'que dia é hoje', 'que horas são' " +
        "e semelhantes. Passe 'utc' como entrada para obter o horário UTC.";

    public Task<string> InvokeAsync(string input, CancellationToken cancellationToken = default)
    {
        var useUtc = !string.IsNullOrWhiteSpace(input) && input.Trim().ToLowerInvariant().Contains("utc");
        var now = _now();
        var value = useUtc ? now.UtcDateTime : now.LocalDateTime;
        var suffix = useUtc ? "UTC" : "horário local";
        var formatted = value.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.GetCultureInfo("pt-BR"));
        return Task.FromResult($"{formatted} ({suffix})");
    }
}
