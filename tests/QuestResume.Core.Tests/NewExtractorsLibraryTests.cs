using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;
using NPOI.HSSF.UserModel;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
using QuestResume.Core.Extraction.Extractors;
using SharpCompress.Common;
using SharpCompress.Writers;

namespace QuestResume.Core.Tests;

/// <summary>
/// Testes reais para os novos extratores que dependem de bibliotecas para gerar a entrada:
/// .sqlite/.db, .xls (NPOI/HSSF), .apk (ZIP), .tgz (SharpCompress) e .parquet (Parquet.Net).
/// </summary>
public class NewExtractorsLibraryTests
{
    private static string TempFile(string extension)
        => Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{extension}");

    // ---------- .sqlite / .db (item 6) ----------

    [Fact]
    public async Task SqliteExtractor_ExtractsSchemaAndSampleRows()
    {
        var path = TempFile(".sqlite");
        try
        {
            using (var conn = new SqliteConnection($"Data Source={path}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText =
                    "CREATE TABLE clientes(id INTEGER PRIMARY KEY, nome TEXT);" +
                    "INSERT INTO clientes(id, nome) VALUES (1, 'Alice'), (2, 'Bob');";
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            var doc = await new SqliteExtractor().ExtractAsync(path);
            Assert.Contains("clientes", doc.Text);
            Assert.Contains("Alice", doc.Text);
            Assert.Contains("Bob", doc.Text);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    // ---------- .xls (item 9) ----------

    [Fact]
    public async Task LegacyOfficeExtractor_ExtractsXlsCells()
    {
        var path = TempFile(".xls");
        try
        {
            using (var workbook = new HSSFWorkbook())
            {
                var sheet = workbook.CreateSheet("Planilha1");
                var row = sheet.CreateRow(0);
                row.CreateCell(0).SetCellValue("Nome");
                row.CreateCell(1).SetCellValue("Maria");
                using var fs = File.Create(path);
                workbook.Write(fs);
            }

            var doc = await new LegacyOfficeExtractor().ExtractAsync(path);
            Assert.Contains("Nome", doc.Text);
            Assert.Contains("Maria", doc.Text);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task LegacyOfficeExtractor_DocReportsLimitedSupport()
    {
        var path = TempFile(".doc");
        try
        {
            await File.WriteAllBytesAsync(path, new byte[] { 0xD0, 0xCF, 0x11, 0xE0 });
            var doc = await new LegacyOfficeExtractor().ExtractAsync(path);
            Assert.True(doc.Metadata.ContainsKey("warning"));
            Assert.Contains("Suporte limitado", doc.Metadata["warning"]);
        }
        finally { File.Delete(path); }
    }

    // ---------- .apk (item 11) ----------

    [Fact]
    public async Task ApkExtractor_ListsPackageEntries()
    {
        var path = TempFile(".apk");
        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("classes.dex");
                using (var writer = new StreamWriter(entry.Open()))
                {
                    writer.Write("conteudo dex ficticio");
                }
                var manifest = archive.CreateEntry("AndroidManifest.xml");
                using (var ms = manifest.Open())
                {
                    var payload = Encoding.Unicode.GetBytes("com.exemplo.app permission");
                    ms.Write(payload, 0, payload.Length);
                }
            }

            var doc = await new ApkExtractor().ExtractAsync(path);
            Assert.Contains("classes.dex", doc.Text);
            Assert.Contains("Conteúdo do pacote APK", doc.Text);
        }
        finally { File.Delete(path); }
    }

    // ---------- .tgz (item 8) ----------

    [Fact]
    public async Task ArchiveExtractor_ExtractsTextFromTarGz()
    {
        var path = TempFile(".tgz");
        try
        {
            using (var fileStream = File.OpenWrite(path))
            using (var writer = WriterFactory.OpenWriter(fileStream, ArchiveType.Tar, new WriterOptions(CompressionType.GZip)))
            {
                var content = Encoding.UTF8.GetBytes("Texto dentro do arquivo compactado.");
                using var entryStream = new MemoryStream(content);
                writer.Write("documento.txt", entryStream, DateTime.UtcNow);
            }

            var doc = await new ArchiveExtractor().ExtractAsync(path);
            Assert.Contains("Texto dentro do arquivo compactado", doc.Text);
            Assert.Contains("documento.txt", doc.Text);
        }
        finally { File.Delete(path); }
    }

    // ---------- .parquet (item 10) ----------

    [Fact]
    public async Task ParquetExtractor_ExtractsSchemaAndRows()
    {
        var path = TempFile(".parquet");
        try
        {
            var field = new DataField<string>("nome");
            var schema = new ParquetSchema(field);

            using (var stream = File.OpenWrite(path))
            using (var writer = await ParquetWriter.CreateAsync(schema, stream))
            using (var rowGroup = writer.CreateRowGroup())
            {
                await rowGroup.WriteColumnAsync(new DataColumn(field, new[] { "Alice", "Bob" }));
            }

            var doc = await new ParquetExtractor().ExtractAsync(path);
            Assert.Contains("nome", doc.Text);
            Assert.Contains("Alice", doc.Text);
            Assert.Contains("Bob", doc.Text);
        }
        finally { File.Delete(path); }
    }
}
