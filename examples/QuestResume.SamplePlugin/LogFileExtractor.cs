using QuestResume.Core.Extraction;
using QuestResume.Core.Models;

namespace QuestResume.SamplePlugin;

/// <summary>
/// Extrator de exemplo para arquivos de log (<c>.log</c>): lê o arquivo como texto puro. Serve
/// como modelo mínimo de um plugin de terceiros — copie este arquivo, ajuste
/// <see cref="SupportedExtensions"/> e a lógica de <see cref="ExtractAsync"/>, compile e coloque
/// a DLL resultante em <c>%LOCALAPPDATA%\QuestResume\plugins</c>.
/// </summary>
public sealed class LogFileExtractor : IExtractorPlugin
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".log" };

    public async Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);

        return new ExtractedDocument
        {
            Path = path,
            FileName = info.Name,
            Extension = info.Extension,
            Text = text,
            ModifiedUtc = info.LastWriteTimeUtc,
            Metadata = new Dictionary<string, string>
            {
                ["plugin"] = nameof(LogFileExtractor)
            }
        };
    }
}
