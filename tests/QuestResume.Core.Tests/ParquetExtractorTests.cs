using Parquet;
using Parquet.Data;
using Parquet.Schema;
using QuestResume.Core.Extraction.Extractors;

namespace QuestResume.Core.Tests;

public class ParquetExtractorTests
{
    [Fact]
    public async Task ExtractAsync_ParquetFile_ReturnsSchemaAndSampleRows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.parquet");

        try
        {
            var nameField = new DataField<string>("nome");
            var ageField = new DataField<int>("idade");
            var schema = new ParquetSchema(nameField, ageField);

            await using (var stream = File.Create(path))
            await using (var writer = await ParquetWriter.CreateAsync(schema, stream))
            {
                using var rowGroup = writer.CreateRowGroup();
                await rowGroup.WriteColumnAsync(new DataColumn(nameField, new[] { "Ana", "Bruno" }));
                await rowGroup.WriteColumnAsync(new DataColumn(ageField, new[] { 30, 25 }));
            }

            var extractor = new ParquetExtractor();
            var document = await extractor.ExtractAsync(path);

            Assert.Contains("nome", document.Text);
            Assert.Contains("Ana", document.Text);
            Assert.Contains("Bruno", document.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
