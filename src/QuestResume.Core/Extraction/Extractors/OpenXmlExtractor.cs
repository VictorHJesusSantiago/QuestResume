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
/// and Excel (.xlsx).
/// </summary>
public sealed class OpenXmlExtractor : IFileExtractor
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".docx", ".pptx", ".xlsx" };

    public Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);

        var text = info.Extension.ToLowerInvariant() switch
        {
            ".docx" => ExtractDocx(path),
            ".pptx" => ExtractPptx(path),
            ".xlsx" => ExtractXlsx(path),
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

    private static string ExtractDocx(string path)
    {
        using var document = WordprocessingDocument.Open(path, false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
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

        return builder.ToString();
    }

    private static string ExtractPptx(string path)
    {
        using var document = PresentationDocument.Open(path, false);
        var presentationPart = document.PresentationPart;
        if (presentationPart is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var slideIndex = 0;
        foreach (var slidePart in presentationPart.SlideParts)
        {
            slideIndex++;
            builder.AppendLine($"[Slide {slideIndex}]");

            foreach (var text in slidePart.Slide?.Descendants<A.Text>() ?? Enumerable.Empty<A.Text>())
            {
                builder.AppendLine(text.Text ?? string.Empty);
            }
        }

        return builder.ToString();
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
}
