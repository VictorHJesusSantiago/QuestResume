using DocumentFormat.OpenXml.ExtendedProperties;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using QuestResume.Core.Extraction.Extractors;

namespace QuestResume.Core.Tests;

public class OpenXmlExtractorTests
{
    [Fact]
    public async Task ExtractAsync_Docx_PrependsCoreAndExtendedMetadata()
    {
        // Item 15: dc:creator (core.xml) e Company (app.xml) devem aparecer como
        // "[Metadados: autor=..., empresa=...]" no início do texto extraído, seguindo o mesmo
        // padrão usado por ImageOcrExtractor para EXIF.
        var path = Path.Combine(Path.GetTempPath(), $"metadados-{Guid.NewGuid()}.docx");

        try
        {
            CreateDocxWithMetadata(path, creator: "Maria Silva", company: "Acme Corp", body: "Conteúdo do documento de teste.");

            var extractor = new OpenXmlExtractor();
            var document = await extractor.ExtractAsync(path);

            Assert.StartsWith("[Metadados:", document.Text);
            Assert.Contains("autor=Maria Silva", document.Text);
            Assert.Contains("empresa=Acme Corp", document.Text);
            Assert.Contains("Conteúdo do documento de teste.", document.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExtractAsync_Docx_WithoutMetadata_DoesNotPrependMetadataBlock()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sem-metadados-{Guid.NewGuid()}.docx");

        try
        {
            CreateDocxWithMetadata(path, creator: null, company: null, body: "Texto simples sem metadados.");

            var extractor = new OpenXmlExtractor();
            var document = await extractor.ExtractAsync(path);

            Assert.DoesNotContain("[Metadados:", document.Text);
            Assert.Contains("Texto simples sem metadados.", document.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void CreateDocxWithMetadata(string path, string? creator, string? company, string body)
    {
        using var document = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document(new Body(new Paragraph(new Run(new Text(body)))));

        if (creator is not null)
        {
            document.PackageProperties.Creator = creator;
            document.PackageProperties.LastModifiedBy = creator;
        }

        if (company is not null)
        {
            var extendedPart = document.AddExtendedFilePropertiesPart();
            extendedPart.Properties = new Properties(new Company(company));
        }

        mainPart.Document.Save();
    }
}
