using QuestResume.Core.Extraction;
using QuestResume.Core.Models;

namespace QuestResume.Core.Extraction.Extractors;

/// <summary>
/// Reads files that are already plain text (or text-ish formats whose raw content is
/// useful enough for search/Q&amp;A without further parsing).
/// </summary>
public sealed class PlainTextExtractor : IFileExtractor
{
    private readonly long _streamingThresholdBytes;

    /// <param name="streamingThresholdBytes">
    /// Acima deste tamanho, o arquivo é lido em stream/chunks (<see cref="StreamReader"/> com
    /// buffer) em vez de carregar todos os bytes de uma vez, evitando o pico de memória de
    /// alocar simultaneamente o <c>byte[]</c> completo e a <c>string</c> resultante. Padrão: 50 MB.
    /// </param>
    public PlainTextExtractor(long streamingThresholdBytes = 50L * 1024 * 1024)
    {
        _streamingThresholdBytes = streamingThresholdBytes;
    }

    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[]
    {
        ".txt", ".csv", ".css", ".js", ".json", ".xml", ".bib", ".tex", ".ics", ".vcf",
        // Source code / config formats: read as plain text and chunked function/class-aware
        // by TextChunker.ChunkCode (see TextChunker.CodeExtensions).
        ".cs", ".py", ".java", ".ts", ".tsx", ".jsx", ".go", ".rb", ".php", ".c", ".cpp",
        ".h", ".hpp", ".rs", ".kt", ".swift", ".sh", ".ps1", ".sql", ".yaml", ".yml", ".md",
        // Arquivos de registro do Windows (.reg): texto UTF-16/ANSI, cobertos pela mesma detecção
        // de encoding usada para os demais formatos de texto (item 8 do Lote 4).
        ".reg"
    };

    public async Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);

        // Encoding detection (item 9, ver EncodingDetector): checa BOM primeiro; sem BOM, tenta
        // UTF-8 estrito e cai para Windows-1252/Latin-1 quando os bytes não são UTF-8 válido —
        // evita corromper arquivos legados/exportados em CP1252 (comuns no Windows).
        // Arquivos grandes: lê em stream/chunks para evitar o pico de memória de File.ReadAllBytes
        // + string completa. StreamReader detecta o BOM (UTF-8/UTF-16); sem BOM cai para UTF-8.
        // O texto resultante ainda é materializado como string (ExtractedDocument.Text), então isto
        // é uma mitigação do pico de alocação, não leitura sem carregar tudo na memória.
        string text;
        if (info.Length > _streamingThresholdBytes)
        {
            text = await ReadLargeFileStreamedAsync(path, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            text = await EncodingDetector.ReadAllTextDetectedAsync(path, cancellationToken).ConfigureAwait(false);
        }

        return new ExtractedDocument
        {
            Path = path,
            FileName = info.Name,
            Extension = info.Extension,
            Text = text,
            ModifiedUtc = info.LastWriteTimeUtc
        };
    }

    private static async Task<string> ReadLargeFileStreamedAsync(string path, CancellationToken cancellationToken)
    {
        const int bufferSize = 128 * 1024;
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
        using var reader = new StreamReader(
            stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize);

        var sb = new System.Text.StringBuilder();
        var buffer = new char[bufferSize];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            sb.Append(buffer, 0, read);
        }

        return sb.ToString();
    }
}
