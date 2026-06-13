using System.Text;
using QuestResume.Core.Models;
using Whisper.net;
using Whisper.net.Wave;

namespace QuestResume.Core.Extraction.Extractors;

/// <summary>
/// Transcribes <c>.wav</c> audio files via Whisper.net. Requires <c>WhisperModelPath</c> to point
/// to a ggml Whisper model; otherwise returns an empty document with a <c>Metadata["warning"]</c>
/// explaining how to set it up, following the same graceful-degradation pattern as the other
/// extractors. The input file must be PCM 16kHz mono (Whisper.net does not resample); other
/// formats/rates produce a warning suggesting an ffmpeg conversion.
/// </summary>
public sealed class AudioTranscriptionExtractor : IFileExtractor, IDisposable
{
    private readonly string _modelPath;
    private WhisperFactory? _factory;
    private bool _initAttempted;
    private string? _initError;

    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".wav" };

    public AudioTranscriptionExtractor(string modelPath)
    {
        _modelPath = modelPath;
    }

    public async Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        var text = string.Empty;
        string? warning;

        if (!EnsureFactory(out warning))
        {
            // warning already set by EnsureFactory
        }
        else
        {
            try
            {
                await using var stream = File.OpenRead(path);
                var parser = new WaveParser(stream, new WaveParserOptions());
                await parser.InitializeAsync(cancellationToken).ConfigureAwait(false);

                if (parser.Channels != 1 || parser.SampleRate != 16000)
                {
                    warning = $"O arquivo de áudio '{info.Name}' precisa estar no formato WAV PCM " +
                              $"16kHz mono (encontrado: {parser.SampleRate}Hz, {parser.Channels} canal(is)). " +
                              $"Converta com 'ffmpeg -i \"{info.Name}\" -ar 16000 -ac 1 saida.wav' e tente novamente.";
                }
                else
                {
                    var samples = await parser.GetAvgSamplesAsync(cancellationToken).ConfigureAwait(false);

                    using var processor = _factory!.CreateBuilder()
                        .WithLanguage("auto")
                        .Build();

                    var sb = new StringBuilder();
                    await foreach (var segment in processor.ProcessAsync(samples, cancellationToken).ConfigureAwait(false))
                    {
                        sb.Append(segment.Text);
                    }

                    text = sb.ToString().Trim();
                    warning = null;
                }
            }
            catch (Exception ex) when (ex is NotSupportedWaveException or CorruptedWaveException)
            {
                warning = $"Não foi possível ler o arquivo de áudio '{info.Name}': {ex.Message}";
            }
        }

        var document = new ExtractedDocument
        {
            Path = path,
            FileName = info.Name,
            Extension = info.Extension,
            Text = text,
            ModifiedUtc = info.LastWriteTimeUtc
        };

        if (warning is not null)
        {
            document.Metadata["warning"] = warning;
        }

        return document;
    }

    private bool EnsureFactory(out string? warning)
    {
        if (_factory is not null)
        {
            warning = null;
            return true;
        }

        if (_initAttempted)
        {
            warning = _initError;
            return false;
        }

        _initAttempted = true;

        if (string.IsNullOrWhiteSpace(_modelPath) || !File.Exists(_modelPath))
        {
            _initError = "Transcrição de áudio habilitada, mas o modelo Whisper (ggml, .bin) não foi " +
                          $"encontrado em '{_modelPath}'. Baixe um modelo em " +
                          "https://huggingface.co/ggerganov/whisper.cpp/tree/main (ex.: ggml-base.bin) " +
                          "e configure WhisperModelPath nas configurações.";
            warning = _initError;
            return false;
        }

        try
        {
            _factory = WhisperFactory.FromPath(_modelPath);
            warning = null;
            return true;
        }
        catch (Exception ex)
        {
            _initError = $"Não foi possível carregar o modelo Whisper: {ex.Message}";
            warning = _initError;
            return false;
        }
    }

    public void Dispose() => _factory?.Dispose();
}
