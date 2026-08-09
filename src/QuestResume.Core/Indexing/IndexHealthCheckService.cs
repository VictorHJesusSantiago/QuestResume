using System.Text;
using Lucene.Net.Index;
using Lucene.Net.Store;
using IODirectory = System.IO.Directory;

namespace QuestResume.Core.Indexing;

public sealed class IndexHealthReport
{
    public bool IsHealthy { get; init; }
    public bool IndexExists { get; init; }
    public IReadOnlyList<string> Problems { get; init; } = Array.Empty<string>();
    public int SegmentCount { get; init; }
    public string Summary { get; init; } = string.Empty;
}

public sealed class IndexHealthCheckService
{
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
                
                checker.FixIndex(status);
            }
        }

        return Check(indexPath);
    }
}
