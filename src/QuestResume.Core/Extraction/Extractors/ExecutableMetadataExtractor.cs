using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Text;
using QuestResume.Core.Models;

namespace QuestResume.Core.Extraction.Extractors;

/// <summary>
/// Extrai metadados pesquisáveis de arquivos executáveis e bibliotecas Windows (.exe, .dll):
/// versão, empresa, descrição (via <see cref="FileVersionInfo"/>) e o timestamp de compilação
/// do cabeçalho COFF (via <see cref="PEReader"/>).
///
/// LIMITAÇÃO TÉCNICA: aplica-se apenas a binários no formato PE (Windows); executáveis ELF
/// (Linux) ou Mach-O (macOS) não são reconhecidos pelo <see cref="PEReader"/> e retornam apenas
/// metadados de sistema de arquivos.
/// </summary>
public sealed class ExecutableMetadataExtractor : IFileExtractor
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".exe", ".dll" };

    public Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        var sb = new StringBuilder();
        var metadata = new Dictionary<string, string>();

        sb.AppendLine($"Arquivo: {info.Name}");
        sb.AppendLine($"Tamanho: {info.Length} bytes");

        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(path);

            if (!string.IsNullOrWhiteSpace(versionInfo.FileVersion))
            {
                sb.AppendLine($"Versão do arquivo: {versionInfo.FileVersion}");
                metadata["fileVersion"] = versionInfo.FileVersion;
            }

            if (!string.IsNullOrWhiteSpace(versionInfo.ProductVersion))
            {
                sb.AppendLine($"Versão do produto: {versionInfo.ProductVersion}");
                metadata["productVersion"] = versionInfo.ProductVersion;
            }

            if (!string.IsNullOrWhiteSpace(versionInfo.CompanyName))
            {
                sb.AppendLine($"Empresa: {versionInfo.CompanyName}");
                metadata["company"] = versionInfo.CompanyName;
            }

            if (!string.IsNullOrWhiteSpace(versionInfo.FileDescription))
            {
                sb.AppendLine($"Descrição: {versionInfo.FileDescription}");
                metadata["description"] = versionInfo.FileDescription;
            }

            if (!string.IsNullOrWhiteSpace(versionInfo.ProductName))
            {
                sb.AppendLine($"Produto: {versionInfo.ProductName}");
                metadata["product"] = versionInfo.ProductName;
            }

            if (!string.IsNullOrWhiteSpace(versionInfo.LegalCopyright))
            {
                sb.AppendLine($"Copyright: {versionInfo.LegalCopyright}");
            }
        }
        catch (Exception ex)
        {
            metadata["versionInfoWarning"] = $"Não foi possível ler informações de versão: {ex.Message}";
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            if (peReader.HasMetadata || peReader.PEHeaders?.CoffHeader is not null)
            {
                var coffHeader = peReader.PEHeaders!.CoffHeader;
                var timestamp = DateTimeOffset.FromUnixTimeSeconds(coffHeader.TimeDateStamp).UtcDateTime;
                sb.AppendLine($"Data de compilação (COFF timestamp): {timestamp:u}");
                metadata["compileTimeUtc"] = timestamp.ToString("u");

                var machine = coffHeader.Machine.ToString();
                sb.AppendLine($"Arquitetura: {machine}");
                metadata["machine"] = machine;

                sb.AppendLine($"Assembly gerenciado (.NET): {(peReader.HasMetadata ? "sim" : "não")}");
                metadata["isManagedAssembly"] = peReader.HasMetadata.ToString();
            }
        }
        catch (Exception ex)
        {
            // Não é um PE válido (ex.: script renomeado para .exe) ou outro formato binário
            // (ELF/Mach-O) — best-effort, não falha a extração inteira.
            metadata["peWarning"] = $"Não foi possível ler o cabeçalho PE: {ex.Message}";
        }

        var document = new ExtractedDocument
        {
            Path = path,
            FileName = info.Name,
            Extension = info.Extension,
            Text = sb.ToString(),
            ModifiedUtc = info.LastWriteTimeUtc,
            Metadata = metadata
        };

        return Task.FromResult(document);
    }
}
