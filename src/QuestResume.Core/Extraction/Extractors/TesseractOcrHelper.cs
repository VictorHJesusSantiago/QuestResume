using System.Globalization;
using System.Text;
using Tesseract;

namespace QuestResume.Core.Extraction.Extractors;

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
