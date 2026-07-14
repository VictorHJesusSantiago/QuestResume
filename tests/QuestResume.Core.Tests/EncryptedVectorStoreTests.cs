using System.Text;
using Microsoft.Data.Sqlite;
using QuestResume.Core.Embeddings;
using QuestResume.Core.Security;

namespace QuestResume.Core.Tests;

public class EncryptedVectorStoreTests
{
    [Fact]
    public void RoundTrip_DecryptsBackToOriginalTextAndEmbedding()
    {
        var indexPath = Path.Combine(Path.GetTempPath(), $"enc-vectorstore-{Guid.NewGuid()}");

        try
        {
            var inner = new VectorStore(indexPath);
            using var store = new EncryptedVectorStore(inner, "senha-super-secreta", new byte[] { 1, 2, 3, 4 });

            var embedding = new float[] { 0.1f, 0.2f, 0.3f, 0.4f };
            store.Add("doc.txt", "doc.txt", 0, "conteúdo confidencial do currículo", embedding);

            var results = store.Search(embedding, topK: 1);

            var result = Assert.Single(results);
            Assert.Equal("conteúdo confidencial do currículo", result.ChunkText);
            Assert.True(result.Score > 0.99f);
        }
        finally
        {
            if (Directory.Exists(indexPath))
            {
                Directory.Delete(indexPath, recursive: true);
            }
        }
    }

    [Fact]
    public void StoredData_IsNotReadableInPlainTextWithoutKey()
    {
        var indexPath = Path.Combine(Path.GetTempPath(), $"enc-vectorstore-{Guid.NewGuid()}");

        try
        {
            using (var inner = new VectorStore(indexPath))
            using (var store = new EncryptedVectorStore(inner, "senha-super-secreta", new byte[] { 9, 9, 9, 9 }))
            {
                store.Add("doc.txt", "doc.txt", 0, "SEGREDO-QUE-NAO-PODE-VAZAR", new float[] { 1f, 0f, 0f, 0f });
            }

            // Read the raw bytes directly from vectors.db, bypassing both VectorStore and
            // EncryptedVectorStore, the way an attacker with filesystem access would.
            string rawText;
            var dbPath = Path.Combine(indexPath, VectorStore.DatabaseFileName);
            using (var connection = new SqliteConnection($"Data Source={dbPath}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT text FROM chunks";
                using var reader = command.ExecuteReader();
                Assert.True(reader.Read());
                rawText = reader.GetString(0);
            }
            SqliteConnection.ClearAllPools();

            Assert.DoesNotContain("SEGREDO-QUE-NAO-PODE-VAZAR", rawText);
        }
        finally
        {
            if (Directory.Exists(indexPath))
            {
                Directory.Delete(indexPath, recursive: true);
            }
        }
    }

    [Fact]
    public void WrongPassword_FailsToDecrypt()
    {
        var indexPath = Path.Combine(Path.GetTempPath(), $"enc-vectorstore-{Guid.NewGuid()}");

        try
        {
            using (var inner = new VectorStore(indexPath))
            using (var store = new EncryptedVectorStore(inner, "senha-correta", new byte[] { 5, 5, 5, 5 }))
            {
                store.Add("doc.txt", "doc.txt", 0, "texto", new float[] { 1f, 0f, 0f, 0f });
            }

            using var innerReopen = new VectorStore(indexPath);
            using var wrongStore = new EncryptedVectorStore(innerReopen, "senha-errada", new byte[] { 5, 5, 5, 5 });

            Assert.ThrowsAny<Exception>(() => wrongStore.Search(new float[] { 1f, 0f, 0f, 0f }, topK: 1));
        }
        finally
        {
            if (Directory.Exists(indexPath))
            {
                Directory.Delete(indexPath, recursive: true);
            }
        }
    }
}

public class AesCryptoHelperTests
{
    [Fact]
    public void Encrypt_Decrypt_RoundTrips()
    {
        var key = MasterKeyManager.DeriveKey("minha-senha", new byte[] { 1, 2, 3 });
        var plaintext = "texto de teste com acentuação";

        var encrypted = AesCryptoHelper.EncryptString(plaintext, key);
        var decrypted = AesCryptoHelper.DecryptString(encrypted, key);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_ProducesCiphertextThatDoesNotContainPlaintext()
    {
        var key = MasterKeyManager.DeriveKey("minha-senha", new byte[] { 1, 2, 3 });
        var plaintext = "INFORMACAO-SIGILOSA";

        var encrypted = AesCryptoHelper.EncryptString(plaintext, key);
        var encryptedAsLatin1 = Encoding.Latin1.GetString(encrypted);

        Assert.DoesNotContain(plaintext, encryptedAsLatin1);
    }
}

public class MasterKeyManagerTests
{
    [Fact]
    public void CreateVerifier_VerifyPassword_RoundTrips()
    {
        var verifier = MasterKeyManager.CreateVerifier("senha123");

        Assert.True(MasterKeyManager.VerifyPassword("senha123", verifier));
        Assert.False(MasterKeyManager.VerifyPassword("senha-errada", verifier));
    }

    [Fact]
    public void CreateVerifier_DoesNotContainThePasswordInPlainText()
    {
        var verifier = MasterKeyManager.CreateVerifier("senha-muito-secreta");

        Assert.DoesNotContain("senha-muito-secreta", verifier);
    }
}
