namespace QuestResume.Api.Services;

/// <summary>
/// Guarda o último texto de progresso reportado pelo <c>IProgress&lt;string&gt;</c> do
/// <c>DocumentIndexer</c> durante a indexação disparada via <c>POST /api/index</c>, para que
/// <c>GET /api/index/progress</c> possa ser consultado (polling) pelo Web UI e refletir uma barra
/// de progresso real enquanto a indexação roda em segundo plano.
///
/// Como o rate limiter "indexing" (ver Program.cs) permite no máximo 1 indexação concorrente por
/// processo, um único slot global é suficiente — não é necessário chavear por usuário/coleção.
/// </summary>
public sealed class IndexingProgressStore
{
    private readonly object _lock = new();
    private string? _lastMessage;
    private bool _running;

    public void Start()
    {
        lock (_lock)
        {
            _running = true;
            _lastMessage = null;
        }
    }

    public void Report(string message)
    {
        lock (_lock)
        {
            _lastMessage = message;
        }
    }

    public void Finish()
    {
        lock (_lock)
        {
            _running = false;
        }
    }

    public IndexingProgressSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            var (current, total) = ParseCounts(_lastMessage);
            return new IndexingProgressSnapshot(_running, _lastMessage, current, total);
        }
    }

    private static (int? Current, int? Total) ParseCounts(string? message)
    {
        if (string.IsNullOrEmpty(message)) return (null, null);
        var match = System.Text.RegularExpressions.Regex.Match(message, @"^\[(\d+)/(\d+)\]");
        if (!match.Success) return (null, null);
        return (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
    }
}

public sealed record IndexingProgressSnapshot(bool Running, string? Message, int? Current, int? Total);
