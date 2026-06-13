using System.Diagnostics;
using System.Text;
using QuestResume.Core.Models;
using Whisper.net;
using Whisper.net.Wave;

namespace QuestResume.Core.Extraction.Extractors;

/// <summary>
/// Transcribes <c>.wav</c> audio files via Whisper.net. Requires <c>WhisperModelPath</c> to point
/// to a ggml Whisper model; otherwise returns an empty document with a <c>Metadata["warning"]</c>
/// explaining how to set it up, following the same graceful-degradation pattern as the other
/// extractors. Whisper.net itself only accepts PCM 16kHz mono; if the input file isn't in that
/// format, this extractor tries to resample it on the fly via <c>ffmpeg</c> (if available on
/// PATH), falling back to a warning with manual conversion instructions if ffmpeg is missing or
/// the conversion fails.
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
        string? resampledPath = null;

        if (!EnsureFactory(out warning))
        {
            // warning already set by EnsureFactory
        }
        else
        {
            try
            {
                var samples = await LoadSamplesAsync(path, cancellationToken).ConfigureAwait(false);

                if (samples is null)
                {
                    resampledPath = await TryResampleWithFfmpegAsync(path, cancellationToken).ConfigureAwait(false);
                    if (resampledPath is not null)
                    {
                        samples = await LoadSamplesAsync(resampledPath, cancellationToken).ConfigureAwait(false);
                    }
                }

                if (samples is null)
                {
                    warning = $"O arquivo de áudio '{info.Name}' precisa estar no formato WAV PCM 16kHz mono. " +
                              "Tentamos convertê-lo automaticamente com ffmpeg, mas isso não foi possível " +
                              "(verifique se o ffmpeg está instalado e disponível no PATH). Você também pode " +
                              $"converter manualmente com 'ffmpeg -i \"{info.Name}\" -ar 16000 -ac 1 saida.wav'.";
                }
                else
                {
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
            finally
            {
                if (resampledPath is not null)
                {
                    File.Delete(resampledPath);
                }
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

    /// <summary>
    /// Reads <paramref name="path"/> as a WAV file and returns its samples, or <c>null</c> if the
    /// file isn't PCM 16kHz mono. Throws <see cref="NotSupportedWaveException"/> or
    /// <see cref="CorruptedWaveException"/> if the file isn't a readable WAV at all.
    /// </summary>
    private static async Task<float[]?> LoadSamplesAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var parser = new WaveParser(stream, new WaveParserOptions());
        await parser.InitializeAsync(cancellationToken).ConfigureAwait(false);

        if (parser.Channels != 1 || parser.SampleRate != 16000)
        {
            return null;
        }

        return await parser.GetAvgSamplesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Tries to resample <paramref name="sourcePath"/> to PCM 16kHz mono using <c>ffmpeg</c>,
    /// returning the path to the resulting temporary file, or <c>null</c> if ffmpeg isn't
    /// available on PATH or the conversion fails.
    /// </summary>
    private static async Task<string?> TryResampleWithFfmpegAsync(string sourcePath, CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"questresume-stt-{Guid.NewGuid()}.wav");

        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(sourcePath);
        startInfo.ArgumentList.Add("-ar");
        startInfo.ArgumentList.Add("16000");
        startInfo.ArgumentList.Add("-ac");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add(tempPath);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode == 0 && File.Exists(tempPath) ? tempPath : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            return null;
        }
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
