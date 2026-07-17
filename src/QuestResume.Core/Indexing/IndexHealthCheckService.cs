using System.Text;
using Lucene.Net.Index;
using Lucene.Net.Store;
using IODirectory = System.IO.Directory;

namespace QuestResume.Core.Indexing;

/// <summary>Relatório de saúde do índice Lucene.</summary>
public sealed class IndexHealthReport
{
    public bool IsHealthy { get; init; }
    public bool IndexExists { get; init; }
    public IReadOnlyList<string> Problems { get; init; } = Array.Empty<string>();
    public int SegmentCount { get; init; }
    public string Summary { get; init; } = string.Empty;
}

/// <summary>
/// Verifica a integridade do índice Lucene rodando <see cref="CheckIndex"/> (utilitário do
/// Lucene.Net) e, quando corrompido, oferece reparo via a própria API de reparo do
/// <see cref="CheckIndex"/> (remove segmentos danificados — pode perder documentos, mas recupera
/// um índice utilizável). Usado pela CLI <c>health-check [--repair]</c> e pela API
/// <c>GET /api/health/index</c> / <c>POST /api/health/index/repair</c>.
/// </summary>
public sealed class IndexHealthCheckService
{
    /// <summary>Abre o índice e roda <see cref="CheckIndex"/> em modo somente-leitura (diagnóstico).</summary>
    public IndexHealthReport Check(string indexPath)
    {
        if (!IODirectory.Exists(indexPath) || !DirectoryReader.IndexExists(FSDirectory.Open(indexPath)))
        {
            return new IndexHealthReport
            {
                IsHealthy = false,
                IndexExists = false,
                Problems = new[] { "Nenhum índice Lucene encontrado no caminho informado." },
                Summary = "Índice inexistente."
            };
        }

        using var dir = FSDirectory.Open(indexPath);
        var log = new StringWriter();
        var checker = new CheckIndex(dir) { InfoStream = log };
        var status = checker.DoCheckIndex();

        var problems = new List<string>();
        if (!status.Clean)
        {
            problems.Add($"{status.TotLoseDocCount} documento(s) potencialmente perdido(s).");
            problems.Add("Índice reportado como não íntegro pelo CheckIndex.");
        }

        return new IndexHealthReport
        {
            IsHealthy = status.Clean,
            IndexExists = true,
            Problems = problems,
            SegmentCount = status.SegmentInfos?.Count ?? 0,
            Summary = status.Clean
                ? $"Índice íntegro ({status.SegmentInfos?.Count ?? 0} segmento(s))."
                : $"Índice com problemas ({problems.Count})."
        };
    }

    /// <summary>
    /// Repara o índice removendo os segmentos danificados (API de reparo do <see cref="CheckIndex"/>).
    /// Só age se o índice estiver realmente corrompido; retorna o relatório pós-reparo.
    /// </summary>
    public IndexHealthReport Repair(string indexPath)
    {
        if (!IODirectory.Exists(indexPath))
        {
            return new IndexHealthReport
            {
                IsHealthy = false,
                IndexExists = false,
                Problems = new[] { "Nenhum índice Lucene encontrado no caminho informado." },
                Summary = "Índice inexistente."
            };
        }

        using (var dir = FSDirectory.Open(indexPath))
        {
            var checker = new CheckIndex(dir) { InfoStream = new StringWriter() };
            var status = checker.DoCheckIndex();
            if (!status.Clean)
            {
                // FixIndex remove os segmentos danificados, tornando o índice utilizável de novo.
                checker.FixIndex(status);
            }
        }

        return Check(indexPath);
    }
}
