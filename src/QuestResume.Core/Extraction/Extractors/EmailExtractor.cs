using System.Text;
using MimeKit;
using QuestResume.Core.Models;
using MsgReader.Outlook;

namespace QuestResume.Core.Extraction.Extractors;

/// <summary>
/// Extracts headers and body text from email messages: .eml (via MimeKit) and
/// Outlook .msg (via MsgReader).
/// </summary>
public sealed class EmailExtractor : IFileExtractor
{
    /// <summary>
    /// Attachment extensions whose content is appended as text alongside the email body.
    /// Binary attachments (images, PDFs, etc.) are still listed by name but not inlined.
    /// </summary>
    private static readonly HashSet<string> TextLikeAttachmentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".csv", ".md", ".json", ".xml", ".html", ".htm", ".log", ".ini",
        ".yaml", ".yml", ".cs", ".py", ".js", ".ts", ".java", ".sql", ".bib", ".tex"
    };

    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".eml", ".msg" };

    public Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);

        var text = info.Extension.ToLowerInvariant() switch
        {
            ".eml" => ExtractEml(path),
            ".msg" => ExtractMsg(path),
            _ => string.Empty
        };

        return Task.FromResult(new ExtractedDocument
        {
            Path = path,
            FileName = info.Name,
            Extension = info.Extension,
            Text = text,
            ModifiedUtc = info.LastWriteTimeUtc
        });
    }

    private static string ExtractEml(string path)
    {
        using var message = MimeMessage.Load(path);

        var builder = new StringBuilder();
        builder.AppendLine($"De: {message.From}");
        builder.AppendLine($"Para: {message.To}");
        builder.AppendLine($"Assunto: {message.Subject}");
        builder.AppendLine($"Data: {message.Date}");
        builder.AppendLine();
        builder.AppendLine(!string.IsNullOrWhiteSpace(message.TextBody)
            ? message.TextBody
            : HtmlExtractor.ExtractVisibleText(message.HtmlBody ?? string.Empty));

        foreach (var attachment in message.Attachments.OfType<MimePart>())
        {
            var fileName = attachment.FileName ?? "anexo";
            builder.AppendLine();
            builder.AppendLine($"--- Anexo: {fileName} ---");

            if (attachment.Content is not null && TextLikeAttachmentExtensions.Contains(Path.GetExtension(fileName)))
            {
                using var contentStream = new MemoryStream();
                attachment.Content.DecodeTo(contentStream);
                contentStream.Position = 0;
                using var reader = new StreamReader(contentStream);
                builder.AppendLine(reader.ReadToEnd());
            }
        }

        return builder.ToString();
    }

    private static string ExtractMsg(string path)
    {
        using var message = new Storage.Message(path);

        var builder = new StringBuilder();
        builder.AppendLine($"De: {message.Sender?.DisplayName} <{message.Sender?.Email}>");
        builder.AppendLine($"Para: {string.Join(", ", message.Recipients.Select(r => r.DisplayName ?? r.Email))}");
        builder.AppendLine($"Assunto: {message.Subject}");
        builder.AppendLine($"Data: {message.SentOn}");
        builder.AppendLine();
        builder.AppendLine(!string.IsNullOrWhiteSpace(message.BodyText)
            ? message.BodyText
            : HtmlExtractor.ExtractVisibleText(message.BodyHtml ?? string.Empty));

        foreach (var attachment in message.Attachments.OfType<Storage.Attachment>())
        {
            var fileName = attachment.FileName ?? "anexo";
            builder.AppendLine();
            builder.AppendLine($"--- Anexo: {fileName} ---");

            if (TextLikeAttachmentExtensions.Contains(Path.GetExtension(fileName)))
            {
                builder.AppendLine(Encoding.UTF8.GetString(attachment.Data));
            }
        }

        return builder.ToString();
    }
}
