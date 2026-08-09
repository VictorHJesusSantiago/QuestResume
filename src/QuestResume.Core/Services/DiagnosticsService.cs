using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using QuestResume.Core.Configuration;
using QuestResume.Core.Indexing;

namespace QuestResume.Core.Services;

public sealed class DiagnosticsService
{
    private readonly ConfigService _configService;

    public DiagnosticsService(ConfigService configService)
    {
        _configService = configService;
    }

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

        public byte[] BuildDiagnosticsZip()
    {
        var options = _configService.Load();
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "resumo.txt", BuildSummary());
            
            WriteEntry(archive, "config-redigida.json", _configService.ExportConfig());

            var health = new IndexHealthCheckService().Check(options.IndexPath);
            WriteEntry(archive, "health-check.json", JsonSerializer.Serialize(health, new JsonSerializerOptions { WriteIndented = true }));

            var log = string.Join("\n", GetRecentLogLines());
            WriteEntry(archive, "log-recente.txt", log);
        }
        return stream.ToArray();
    }

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
