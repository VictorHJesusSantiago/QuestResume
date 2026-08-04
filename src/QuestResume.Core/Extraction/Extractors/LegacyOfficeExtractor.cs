using System.Text;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using QuestResume.Core.Models;

namespace QuestResume.Core.Extraction.Extractors;

public sealed class LegacyOfficeExtractor : IFileExtractor
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".doc", ".xls", ".ppt" };

    public Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        var text = string.Empty;
        var warnings = new List<string>();

        try
        {
            switch (info.Extension.ToLowerInvariant())
            {
                case ".xls":
                    text = ExtractXls(path);
                    break;
                case ".doc":
                    warnings.Add("Suporte limitado: arquivos Word 97-2003 (.doc) binários não podem ser " +
                                 "extraídos sem o módulo HWPF, ausente na port .NET do NPOI. Converta para .docx " +
                                 "para indexar o conteúdo.");
                    break;
                case ".ppt":
                    warnings.Add("Suporte limitado: arquivos PowerPoint 97-2003 (.ppt) binários não podem ser " +
                                 "extraídos sem o módulo HSLF, ausente na port .NET do NPOI. Converta para .pptx " +
                                 "para indexar o conteúdo.");
                    break;
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Falha ao extrair arquivo Office legado ({info.Extension}): {ex.Message}");
        }

        var document = new ExtractedDocument
        {
            Path = path,
            FileName = info.Name,
            Extension = info.Extension,
            Text = text,
            ModifiedUtc = info.LastWriteTimeUtc
        };

        if (warnings.Count > 0)
        {
            document.Metadata["warning"] = string.Join(" | ", warnings);
        }

        return Task.FromResult(document);
    }

    private static string ExtractXls(string path)
    {
        using var stream = File.OpenRead(path);
        using var workbook = new HSSFWorkbook(stream);
        var builder = new StringBuilder();

        for (var sheetIndex = 0; sheetIndex < workbook.NumberOfSheets; sheetIndex++)
        {
            var sheet = workbook.GetSheetAt(sheetIndex);
            builder.AppendLine($"--- {sheet.SheetName} ---");

            for (var rowIndex = sheet.FirstRowNum; rowIndex <= sheet.LastRowNum; rowIndex++)
            {
                var row = sheet.GetRow(rowIndex);
                if (row is null)
                {
                    continue;
                }

                var cells = new List<string>();
                for (var cellIndex = row.FirstCellNum; cellIndex < row.LastCellNum; cellIndex++)
                {
                    var cell = row.GetCell(cellIndex);
                    cells.Add(cell?.ToString() ?? string.Empty);
                }

                builder.AppendLine(string.Join("\t", cells));
            }
        }

        return builder.ToString().Trim();
    }
}
