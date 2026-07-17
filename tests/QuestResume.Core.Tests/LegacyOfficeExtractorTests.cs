using NPOI.HSSF.UserModel;
using QuestResume.Core.Extraction.Extractors;

namespace QuestResume.Core.Tests;

public class LegacyOfficeExtractorTests
{
    [Fact]
    public async Task ExtractAsync_XlsWorkbook_ExtractsCellText()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.xls");

        try
        {
            using (var stream = File.Create(path))
            {
                var workbook = new HSSFWorkbook();
                var sheet = workbook.CreateSheet("Planilha1");
                var row = sheet.CreateRow(0);
                row.CreateCell(0).SetCellValue("Conteúdo de teste");
                workbook.Write(stream, leaveOpen: false);
            }

            var extractor = new LegacyOfficeExtractor();
            var document = await extractor.ExtractAsync(path);

            Assert.Contains("Conteúdo de teste", document.Text);
            Assert.Contains("Planilha1", document.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExtractAsync_DocFile_ReturnsLimitedSupportWarning()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.doc");

        try
        {
            // Não é um .doc binário real (HWPF não está disponível na port .NET do NPOI) — o
            // extrator deve degradar graciosamente com um aviso em PT-BR, em vez de lançar.
            await File.WriteAllTextAsync(path, "conteúdo qualquer");

            var extractor = new LegacyOfficeExtractor();
            var document = await extractor.ExtractAsync(path);

            Assert.True(document.Metadata.ContainsKey("warning"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
