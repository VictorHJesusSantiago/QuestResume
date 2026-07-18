using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using QuestResume.Core.Configuration;
using QuestResume.Core.Indexing;

namespace QuestResume.Core.Services;

/// <summary>
/// Coleta informações de diagnóstico (versão da app, SO, config com segredos redigidos, últimas
/// linhas do log Serilog e resultado do health-check do índice) e as empacota num <c>.zip</c>.
/// Usado pela CLI <c>diagnostics export</c> e pela API <c>GET /api/diagnostics/export</c>.
/// </summary>
public sealed class DiagnosticsService
{
    private readonly ConfigService _configService;

    public DiagnosticsService(ConfigService configService)
    {
        _configService = configService;
    }

    /// <summary>Monta um resumo textual do diagnóstico (sem segredos).</summary>
    public string BuildSummary()
    {
        var options = _configService.Load();
        var sb = new StringBuilder();
        sb.AppendLine("QuestResume — Diagnóstico");
        sb.AppendLine($"Gerado em (UTC): {DateTime.UtcNow:u}");
        sb.AppendLine($"Versão da app: {Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "desconhecida"}");
        sb.AppendLine($"Sistema operacional: {Environment.OSVersion}");
        sb.AppendLine($".NET: {Environment.Version}");
        sb.AppendLine($"Arquitetura: {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}");
        sb.AppendLine($"Modo portátil: {(ConfigService.IsPortableMode() ? "sim" : "não")}");
        sb.AppendLine($"IndexPath: {options.IndexPath}");
        return sb.ToString();
    }

    /// <summary>Retorna as últimas <paramref name="maxLines"/> linhas do arquivo de log mais recente.</summary>
    public IReadOnlyList<string> GetRecentLogLines(int maxLines = 200)
    {
        try
        {
            var logsDir = ConfigService.GetDefaultLogsPath();
            if (!Directory.Exists(logsDir)) return Array.Empty<string>();
            var latest = Directory.EnumerateFiles(logsDir, "log-*.txt")
                .OrderByDescending(f => f, StringComparer.Ordinal)
                .FirstOrDefault();
            if (latest is null) return Array.Empty<string>();

            using var stream = new FileStream(latest, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var all = reader.ReadToEnd().Replace("\r\n", "\n").Split('\n');
            return all.Reverse().Take(maxLines).Reverse().ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>Gera o pacote .zip de diagnóstico em memória.</summary>
    public byte[] BuildDiagnosticsZip()
    {
        var options = _configService.Load();
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "resumo.txt", BuildSummary());
            // Config com segredos redigidos (reaproveita item 17).
            WriteEntry(archive, "config-redigida.json", _configService.ExportConfig());

            var health = new IndexHealthCheckService().Check(options.IndexPath);
            WriteEntry(archive, "health-check.json", JsonSerializer.Serialize(health, new JsonSerializerOptions { WriteIndented = true }));

            var log = string.Join("\n", GetRecentLogLines());
            WriteEntry(archive, "log-recente.txt", log);
        }
        return stream.ToArray();
    }

    /// <summary>Escreve o zip de diagnóstico no caminho informado.</summary>
    public void ExportToFile(string zipPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(zipPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(zipPath, BuildDiagnosticsZip());
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
