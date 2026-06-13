using Tesseract;

namespace QuestResume.Core.Extraction.Extractors;

/// <summary>
/// Shared lazy-initialized Tesseract 5 engine wrapper used by <see cref="ImageOcrExtractor"/>
/// and the OCR fallback in <see cref="PdfExtractor"/>. The engine is only created on first
/// use, so constructing it without a valid <c>tessdata</c> folder never throws.
/// </summary>
internal sealed class TesseractOcrHelper : IDisposable
{
    private readonly string _tessDataPath;
    private readonly string _languages;
    private TesseractEngine? _engine;
    private bool _initAttempted;
    private string? _initError;

    public TesseractOcrHelper(string tessDataPath, string languages)
    {
        _tessDataPath = tessDataPath;
        _languages = string.IsNullOrWhiteSpace(languages) ? "por+eng" : languages;
    }

    /// <summary>
    /// Runs OCR over the given image bytes (PNG, JPEG, TIFF, BMP or GIF). Returns
    /// <c>null</c> and sets <paramref name="warning"/> (PT-BR) if the Tesseract engine
    /// could not be initialized.
    /// </summary>
    public string? TryOcr(byte[] imageBytes, out string? warning)
    {
        if (!EnsureEngine(out warning))
        {
            return null;
        }

        using var pix = Pix.LoadFromMemory(imageBytes);
        using var page = _engine!.Process(pix);
        return page.GetText();
    }

    private bool EnsureEngine(out string? warning)
    {
        if (_engine is not null)
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

        if (string.IsNullOrWhiteSpace(_tessDataPath) || !Directory.Exists(_tessDataPath))
        {
            _initError = "OCR habilitado, mas a pasta de dados do Tesseract (tessdata) não foi " +
                          $"encontrada em '{_tessDataPath}'. Baixe os arquivos de idioma do Tesseract 5 " +
                          "(ex.: https://github.com/tesseract-ocr/tessdata) e configure TessDataPath " +
                          "nas configurações.";
            warning = _initError;
            return false;
        }

        try
        {
            _engine = new TesseractEngine(_tessDataPath, _languages, EngineMode.Default);
            warning = null;
            return true;
        }
        catch (Exception ex)
        {
            _initError = $"Não foi possível inicializar o OCR (Tesseract): {ex.Message}";
            warning = _initError;
            return false;
        }
    }

    public void Dispose() => _engine?.Dispose();
}
