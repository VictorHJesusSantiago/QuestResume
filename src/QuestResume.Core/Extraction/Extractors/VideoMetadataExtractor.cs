using System.Text;
using QuestResume.Core.Models;

namespace QuestResume.Core.Extraction.Extractors;

/// <summary>
/// Extrai metadados pesquisáveis de arquivos de vídeo (.mp4, .mkv, .avi, .mov).
///
/// LIMITAÇÃO TÉCNICA: não há parsing completo de contêineres MKV/AVI nesta implementação
/// (exigiria bibliotecas pesadas como FFmpeg/MediaInfo, fora do escopo de um extrator leve
/// sem dependências nativas). Para .mkv e .avi apenas metadados do sistema de arquivos
/// (tamanho, datas) são extraídos. Para .mp4/.mov, os átomos ISO-BMFF <c>moov/mvhd</c> são
/// lidos manualmente para obter duração e timescale, sem depender de bibliotecas externas.
/// </summary>
public sealed class VideoMetadataExtractor : IFileExtractor
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } =
        new[] { ".mp4", ".mkv", ".avi", ".mov" };

    public Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        var sb = new StringBuilder();
        sb.AppendLine($"Arquivo de vídeo: {info.Name}");
        sb.AppendLine($"Tamanho: {info.Length} bytes ({info.Length / 1024.0 / 1024.0:F2} MB)");
        sb.AppendLine($"Criado em: {info.CreationTimeUtc:u}");
        sb.AppendLine($"Modificado em: {info.LastWriteTimeUtc:u}");

        var extension = info.Extension.ToLowerInvariant();
        var metadata = new Dictionary<string, string>
        {
            ["sizeBytes"] = info.Length.ToString(),
            ["createdUtc"] = info.CreationTimeUtc.ToString("u"),
            ["modifiedUtc"] = info.LastWriteTimeUtc.ToString("u")
        };

        if (extension is ".mp4" or ".mov")
        {
            var duration = TryReadMp4Duration(path);
            if (duration is not null)
            {
                sb.AppendLine($"Duração: {duration.Value:g}");
                metadata["durationSeconds"] = duration.Value.TotalSeconds.ToString("F1");
            }
            else
            {
                sb.AppendLine("Duração: não foi possível determinar (átomo moov/mvhd ausente ou arquivo corrompido).");
            }
        }
        else
        {
            sb.AppendLine("Duração: indisponível — parsing completo de contêineres MKV/AVI não implementado " +
                           "(requer bibliotecas externas como FFmpeg/MediaInfo); apenas metadados de sistema de arquivos estão disponíveis.");
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

    /// <summary>
    /// Lê o átomo ISO-BMFF <c>moov/mvhd</c> de um arquivo MP4/MOV para calcular a duração,
    /// sem depender de bibliotecas externas. Retorna <c>null</c> se o átomo não for encontrado
    /// ou o arquivo não seguir a estrutura esperada.
    /// </summary>
    private static TimeSpan? TryReadMp4Duration(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var mvhd = FindAtom(stream, "moov", stream.Length);
            if (mvhd is null) return null;

            using var moovStream = stream;
            moovStream.Seek(mvhd.Value.Start, SeekOrigin.Begin);
            var inner = FindAtom(moovStream, "mvhd", mvhd.Value.Start + mvhd.Value.Size);
            if (inner is null) return null;

            moovStream.Seek(inner.Value.Start, SeekOrigin.Begin);
            var version = moovStream.ReadByte();
            moovStream.Seek(3, SeekOrigin.Current); // flags

            uint timescale;
            ulong duration;
            if (version == 1)
            {
                moovStream.Seek(16, SeekOrigin.Current); // creation+modification (64-bit each)
                timescale = ReadUInt32BE(moovStream);
                duration = ReadUInt64BE(moovStream);
            }
            else
            {
                moovStream.Seek(8, SeekOrigin.Current); // creation+modification (32-bit each)
                timescale = ReadUInt32BE(moovStream);
                duration = ReadUInt32BE(moovStream);
            }

            if (timescale == 0) return null;
            return TimeSpan.FromSeconds((double)duration / timescale);
        }
        catch
        {
            return null;
        }
    }

    private static (long Start, long Size)? FindAtom(Stream stream, string fourCc, long limit)
    {
        while (stream.Position < limit)
        {
            var atomStart = stream.Position;
            var sizeBytes = new byte[4];
            if (stream.Read(sizeBytes, 0, 4) != 4) return null;
            var size = (long)ReadUInt32BE(sizeBytes);

            var typeBytes = new byte[4];
            if (stream.Read(typeBytes, 0, 4) != 4) return null;
            var type = Encoding.ASCII.GetString(typeBytes);

            if (size < 8) return null; // extended/zero-size atoms not handled by this lightweight reader

            if (type == fourCc)
            {
                return (atomStart + 8, size - 8);
            }

            stream.Seek(atomStart + size, SeekOrigin.Begin);
        }

        return null;
    }

    private static uint ReadUInt32BE(byte[] bytes) =>
        (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);

    private static uint ReadUInt32BE(Stream stream)
    {
        var bytes = new byte[4];
        stream.Read(bytes, 0, 4);
        return ReadUInt32BE(bytes);
    }

    private static ulong ReadUInt64BE(Stream stream)
    {
        var bytes = new byte[8];
        stream.Read(bytes, 0, 8);
        ulong value = 0;
        foreach (var b in bytes) value = (value << 8) | b;
        return value;
    }
}
