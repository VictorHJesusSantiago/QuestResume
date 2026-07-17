using System.Text;
using QuestResume.Core.Models;
using XstReader;

namespace QuestResume.Core.Extraction.Extractors;

/// <summary>
/// Extracts e-mails from Outlook data files (.pst, .ost) via the XstReader.Api NuGet library.
/// The whole file is treated as a single "container" document (matching the pattern used by
/// <see cref="ZipArchiveExtractor"/> for other multi-item containers): every message across
/// every folder is concatenated, each separated by a clear header with subject/sender/date, so
/// downstream chunking still finds each e-mail's content contiguous. If the file can't be
/// opened or parsed (corrupt file, unsupported PST variant, password-protected, etc.), falls
/// back to basic file metadata with a clear PT-BR warning rather than throwing.
/// </summary>
public sealed class PstOstExtractor : IFileExtractor
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".pst", ".ost" };

    public Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        var builder = new StringBuilder();
        var warnings = new List<string>();
        var emailCount = 0;

        try
        {
            using var xstFile = new XstFile(path);
            EmitFolder(xstFile.RootFolder, builder, ref emailCount, cancellationToken);

            if (emailCount == 0)
            {
                warnings.Add("Arquivo PST/OST aberto com sucesso, mas nenhuma mensagem foi encontrada.");
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Falha ao ler arquivo Outlook (.pst/.ost) — pode estar corrompido, protegido por " +
                         $"senha ou em uma variante não suportada pela biblioteca XstReader: {ex.Message}. " +
                         $"Apenas metadados básicos do arquivo foram registrados.");
        }

        var document = new ExtractedDocument
        {
            Path = path,
            FileName = info.Name,
            Extension = info.Extension,
            Text = builder.ToString(),
            ModifiedUtc = info.LastWriteTimeUtc
        };

        document.Metadata["emailCount"] = emailCount.ToString();
        if (warnings.Count > 0)
        {
            document.Metadata["warning"] = string.Join(" | ", warnings);
        }

        return Task.FromResult(document);
    }

    private static void EmitFolder(XstFolder folder, StringBuilder builder, ref int emailCount, CancellationToken cancellationToken)
    {
        foreach (var message in folder.GetMessages())
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? bodyText = null;
            try
            {
                bodyText = message.GetBody()?.Text;
            }
            catch
            {
                // Best-effort: some messages (e.g. encrypted/odd formats) may fail to decode the body.
            }

            builder.AppendLine("--- E-mail ---");
            builder.AppendLine($"Assunto: {message.Subject}");
            builder.AppendLine($"De: {message.From}");
            builder.AppendLine($"Para: {message.To}");
            if (message.Date.HasValue)
            {
                builder.AppendLine($"Data: {message.Date.Value:yyyy-MM-dd HH:mm:ss}");
            }
            builder.AppendLine();
            builder.AppendLine(bodyText ?? string.Empty);
            builder.AppendLine();

            emailCount++;
        }

        foreach (var subFolder in folder.GetFolders())
        {
            EmitFolder(subFolder, builder, ref emailCount, cancellationToken);
        }
    }
}
