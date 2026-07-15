using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using QuestResume.Core.Models;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace QuestResume.Core.Extraction.Extractors;

/// <summary>
/// Extracts text from Office Open XML documents: Word (.docx), PowerPoint (.pptx)
/// and Excel (.xlsx). Also extracts OOXML core/extended document properties (autor, empresa,
/// datas, revisão — item 15) and, when OCR is enabled, runs OCR over images embedded in
/// .docx/.pptx (item 17), appending the recognized text.
/// </summary>
public sealed class OpenXmlExtractor : IFileExtractor, IDisposable
{
    private readonly bool _ocrEnabled;
    private readonly TesseractOcrHelper? _ocr;

    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".docx", ".pptx", ".xlsx" };

    public OpenXmlExtractor(bool ocrEnabled = false, string tessDataPath = "", string ocrLanguages = "por+eng")
    {
        _ocrEnabled = ocrEnabled;
        _ocr = ocrEnabled ? new TesseractOcrHelper(tessDataPath, ocrLanguages) : null;
    }

    public Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        string? ocrWarning = null;

        var text = info.Extension.ToLowerInvariant() switch
        {
            ".docx" => ExtractDocx(path, out ocrWarning),
            ".pptx" => ExtractPptx(path, out ocrWarning),
            ".xlsx" => ExtractXlsx(path),
            _ => string.Empty
        };

        var document = new ExtractedDocument
        {
            Path = path,
            FileName = info.Name,
            Extension = info.Extension,
            Text = text,
            ModifiedUtc = info.LastWriteTimeUtc
        };

        if (ocrWarning is not null)
        {
            document.Metadata["warning"] = ocrWarning;
        }

        return Task.FromResult(document);
    }

    /// <summary>
    /// Item 15: reads the OOXML core (dc:creator, dcterms:created/modified, cp:lastModifiedBy,
    /// cp:revision) and extended (Company) properties, following the same
    /// <c>[Metadados: ...]</c> prefix pattern used for EXIF data in <see cref="ImageOcrExtractor"/>.
    /// Returns <c>null</c> when the document has none of these properties set.
    /// </summary>
    private static string? BuildMetadataSummary(OpenXmlPackage package, string? company)
    {
        var props = package.PackageProperties;
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(props.Creator))
        {
            parts.Add($"autor={props.Creator}");
        }

        if (!string.IsNullOrWhiteSpace(company))
        {
            parts.Add($"empresa={company}");
        }

        if (props.Created.HasValue)
        {
            parts.Add($"criado={props.Created.Value:yyyy-MM-dd}");
        }

        if (props.Modified.HasValue)
        {
            parts.Add($"modificado={props.Modified.Value:yyyy-MM-dd}");
        }

        if (!string.IsNullOrWhiteSpace(props.LastModifiedBy))
        {
            parts.Add($"últimaModificaçãoPor={props.LastModifiedBy}");
        }

        if (!string.IsNullOrWhiteSpace(props.Revision))
        {
            parts.Add($"revisão={props.Revision}");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }

    private string ExtractDocx(string path, out string? ocrWarning)
    {
        ocrWarning = null;
        using var document = WordprocessingDocument.Open(path, false);
        var body = document.MainDocumentPart?.Document?.Body;

        var builder = new StringBuilder();

        var company = document.ExtendedFilePropertiesPart?.Properties?.Company?.Text;
        var metadataSummary = BuildMetadataSummary(document, company);
        if (metadataSummary is not null)
        {
            builder.AppendLine($"[Metadados: {metadataSummary}]");
        }

        if (body is not null)
        {
            foreach (var paragraph in body.Descendants<W.Paragraph>())
            {
                builder.AppendLine(paragraph.InnerText);
            }

            foreach (var table in body.Descendants<W.Table>())
            {
                foreach (var row in table.Descendants<W.TableRow>())
                {
                    var cells = row.Descendants<W.TableCell>().Select(c => c.InnerText);
                    builder.AppendLine(string.Join('\t', cells));
                }
            }
        }

        if (_ocrEnabled && _ocr is not null && document.MainDocumentPart is not null)
        {
            AppendImageOcrText(builder, document.MainDocumentPart.ImageParts, ref ocrWarning);
        }

        return builder.ToString();
    }

    private string ExtractPptx(string path, out string? ocrWarning)
    {
        ocrWarning = null;
        using var document = PresentationDocument.Open(path, false);
        var presentationPart = document.PresentationPart;

        var builder = new StringBuilder();

        var company = document.ExtendedFilePropertiesPart?.Properties?.Company?.Text;
        var metadataSummary = BuildMetadataSummary(document, company);
        if (metadataSummary is not null)
        {
            builder.AppendLine($"[Metadados: {metadataSummary}]");
        }

        if (presentationPart is not null)
        {
            var slideIndex = 0;
            foreach (var slidePart in presentationPart.SlideParts)
            {
                slideIndex++;
                builder.AppendLine($"[Slide {slideIndex}]");

                foreach (var text in slidePart.Slide?.Descendants<A.Text>() ?? Enumerable.Empty<A.Text>())
                {
                    builder.AppendLine(text.Text ?? string.Empty);
                }

                if (_ocrEnabled && _ocr is not null)
                {
                    AppendImageOcrText(builder, slidePart.ImageParts, ref ocrWarning);
                }
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Item 17: runs OCR (via the shared <see cref="TesseractOcrHelper"/>) over every embedded
    /// image part (media files under <c>word/media/</c> or <c>ppt/media/</c> inside the OOXML
    /// zip package) and appends the recognized text, so scanned figures/screenshots pasted into
    /// a Word/PowerPoint document become searchable too.
    /// </summary>
    private void AppendImageOcrText(StringBuilder builder, IEnumerable<ImagePart> imageParts, ref string? ocrWarning)
    {
        foreach (var imagePart in imageParts)
        {
            using var stream = imagePart.GetStream();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);

            var text = _ocr!.TryOcr(memory.ToArray(), out var warning);
            ocrWarning ??= warning;

            if (!string.IsNullOrWhiteSpace(text))
            {
                builder.AppendLine($"[OCR de imagem incorporada]");
                builder.AppendLine(text);
            }
        }
    }

    private static string ExtractXlsx(string path)
    {
        using var document = SpreadsheetDocument.Open(path, false);
        var workbookPart = document.WorkbookPart;
        if (workbookPart is null)
        {
            return string.Empty;
        }

        var sharedStrings = workbookPart.GetPartsOfType<SharedStringTablePart>().FirstOrDefault()?.SharedStringTable;
        var builder = new StringBuilder();

        var company = document.ExtendedFilePropertiesPart?.Properties?.Company?.Text;
        var metadataSummary = BuildMetadataSummary(document, company);
        if (metadataSummary is not null)
        {
            builder.AppendLine($"[Metadados: {metadataSummary}]");
        }

        var sheets = workbookPart.Workbook?.Descendants<Sheet>() ?? Enumerable.Empty<Sheet>();
        foreach (var sheet in sheets)
        {
            if (sheet.Id?.Value is null)
            {
                continue;
            }

            var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id.Value);
            builder.AppendLine($"[Sheet: {sheet.Name}]");

            foreach (var row in worksheetPart.Worksheet?.Descendants<Row>() ?? Enumerable.Empty<Row>())
            {
                var values = row.Elements<Cell>().Select(cell => GetCellValue(cell, sharedStrings));
                builder.AppendLine(string.Join('\t', values));
            }
        }

        return builder.ToString();
    }

    private static string GetCellValue(Cell cell, SharedStringTable? sharedStrings)
    {
        var value = cell.CellValue?.InnerText ?? string.Empty;

        if (cell.DataType?.Value == CellValues.SharedString && sharedStrings is not null
            && int.TryParse(value, out var index))
        {
            return sharedStrings.ElementAtOrDefault(index)?.InnerText ?? string.Empty;
        }

        return value;
    }

    public void Dispose() => _ocr?.Dispose();
}
