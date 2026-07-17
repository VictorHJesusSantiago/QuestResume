using Microsoft.Data.Sqlite;
using QuestResume.Core.Extraction.Extractors;

namespace QuestResume.Core.Tests;

public class SqliteExtractorTests
{
    [Fact]
    public async Task ExtractAsync_SqliteDb_ReturnsSchemaAndSampleRows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.sqlite");

        try
        {
            using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                using var create = connection.CreateCommand();
                create.CommandText = "CREATE TABLE pessoas (id INTEGER PRIMARY KEY, nome TEXT);";
                create.ExecuteNonQuery();

                using var insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO pessoas (nome) VALUES ('Ana'), ('Bruno');";
                insert.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            var extractor = new SqliteExtractor();
            var document = await extractor.ExtractAsync(path);

            Assert.Contains("pessoas", document.Text);
            Assert.Contains("Ana", document.Text);
            Assert.Contains("Bruno", document.Text);
            Assert.False(document.Metadata.ContainsKey("warning"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }
}
