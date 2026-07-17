using System.Globalization;
using System.Text;
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
    ///
    /// Two quality improvements over a bare <c>Process(pix).GetText()</c> call (item 13/14 of
    /// Lote 4):
    /// 1) Deskew: before OCR, <see cref="Pix.Deskew()"/> (Leptonica) detects and corrects small
    ///    page rotation/inclination (scans/photographed documents). Best-effort — if deskewing
    ///    fails or the image is already straight, the original pixels are used unchanged.
    /// 2) Layout-aware text order: instead of Tesseract's default reading-order concatenation
    ///    (which can interleave multi-column text), the word-level TSV output (with bounding
    ///    boxes) is grouped by Tesseract's own block/paragraph/line numbering, and an extra
    ///    blank line is inserted between blocks whose horizontal start differs enough to look
    ///    like a column/section boundary — preserving column and block breaks in the output.
    /// </summary>
    public string? TryOcr(byte[] imageBytes, out string? warning)
    {
        if (!EnsureEngine(out warning))
        {
            return null;
        }

        using var originalPix = Pix.LoadFromMemory(imageBytes);
        var pix = originalPix;
        Pix? deskewedPix = null;

        try
        {
            deskewedPix = originalPix.Deskew();
            if (deskewedPix is not null)
            {
                pix = deskewedPix;
            }
        }
        catch
        {
            // Best-effort: deskew failure just means we OCR the original (possibly skewed) image.
        }

        try
        {
            using var page = _engine!.Process(pix);
            var tsv = page.GetTsvText(1);
            var layoutText = TryBuildLayoutPreservingText(tsv);
            return layoutText ?? page.GetText();
        }
        finally
        {
            deskewedPix?.Dispose();
        }
    }

    /// <summary>
    /// Parses Tesseract's TSV output (level, page, block, par, line, word, left, top, width,
    /// height, conf, text — tab-separated, one header line) and rebuilds the text preserving
    /// column/block breaks: consecutive words on the same block/paragraph/line are joined with a
    /// single space; a new line starts on block/paragraph/line change; and a blank line (\n\n) is
    /// inserted between blocks whose left edge jumps by more than ~15% of the line width from the
    /// previous block, a simple heuristic for a multi-column layout change. Returns null if the
    /// TSV can't be parsed (falls back to the caller's default word-order text).
    /// </summary>
    private static string? TryBuildLayoutPreservingText(string tsv)
    {
        if (string.IsNullOrWhiteSpace(tsv)) return null;

        var lines = tsv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return null;

        var builder = new StringBuilder();
        var currentKey = (block: -1, par: -1, line: -1);
        var currentLineWords = new List<string>();
        var lastLeft = -1;
        var lastBlock = -1;

        void FlushLine()
        {
            if (currentLineWords.Count == 0) return;
            builder.AppendLine(string.Join(' ', currentLineWords));
            currentLineWords.Clear();
        }

        // Skip header row (index 0).
        for (var i = 1; i < lines.Length; i++)
        {
            var fields = lines[i].Split('\t');
            if (fields.Length < 12) continue;

            if (!int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var block)) continue;
            if (!int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var par)) continue;
            if (!int.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var line)) continue;
            if (!int.TryParse(fields[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var left)) continue;
            if (!int.TryParse(fields[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)) continue;

            var word = fields[11].Trim();
            if (word.Length == 0) continue;

            var key = (block, par, line);
            if (key != currentKey)
            {
                FlushLine();

                if (lastBlock != -1 && block != lastBlock && lastLeft >= 0 && width > 0
                    && Math.Abs(left - lastLeft) > width * 1.5)
                {
                    builder.AppendLine();
                }

                currentKey = key;
                lastBlock = block;
                lastLeft = left;
            }

            currentLineWords.Add(word);
        }

        FlushLine();

        var result = builder.ToString().Trim();
        return result.Length > 0 ? result : null;
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
