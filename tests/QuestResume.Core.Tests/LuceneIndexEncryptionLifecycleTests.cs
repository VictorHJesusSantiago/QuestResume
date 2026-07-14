using System.Security.Cryptography;
using QuestResume.Core.Indexing;
using QuestResume.Core.Security;

namespace QuestResume.Core.Tests;

/// <summary>
/// End-to-end coverage of the Lucene index encryption open/close lifecycle wired into
/// <see cref="DocumentIndexer.IndexFolderAsync"/> (ON OPEN: decrypt index.enc if present; ON
/// CLOSE: re-encrypt and delete plaintext) and <see cref="SearchService"/>'s master-password
/// constructor overload / <see cref="SearchService.SealAsync"/>.
/// </summary>
public class LuceneIndexEncryptionLifecycleTests
{
    private const string MasterPassword = "senha-mestre-de-teste-123";

    [Fact]
    public async Task IndexFolderAsync_WithEncryption_LeavesOnlyIndexEncOnDisk_NoPlaintextLuceneFiles()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"enc-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"enc-index-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "curriculo.txt"), "Experiencia com criptografia de indices Lucene.");

            var verifier = MasterKeyManager.CreateVerifier(MasterPassword);
            var indexer = new DocumentIndexer();
            var stats = await indexer.IndexFolderAsync(folder, indexPath, masterPassword: MasterPassword, masterKeyVerifier: verifier);

            Assert.Equal(1, stats.FilesProcessed);

            // No plaintext Lucene segment files (segments_N, .cfs, .cfe, .si, etc.) should remain
            // on disk once the indexing cycle completes — only the encrypted bundle.
            Assert.Empty(Directory.GetFiles(indexPath, "segments_*"));
            Assert.Empty(Directory.GetFiles(indexPath, "*.cfs"));
            Assert.Empty(Directory.GetFiles(indexPath, "*.cfe"));
            Assert.Empty(Directory.GetFiles(indexPath, "*.si"));
            Assert.True(File.Exists(Path.Combine(indexPath, LuceneIndexEncryptionService.EncryptedFileName)));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            if (Directory.Exists(indexPath)) Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task SearchService_WithCorrectPassword_DecryptsAndFindsIndexedContent()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"enc-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"enc-index-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "curriculo.txt"), "Palavra-chave exclusiva: xilofoneabacaxi.");

            var verifier = MasterKeyManager.CreateVerifier(MasterPassword);
            var indexer = new DocumentIndexer();
            await indexer.IndexFolderAsync(folder, indexPath, masterPassword: MasterPassword, masterKeyVerifier: verifier);

            // Reopening with the correct password must transparently decrypt index.enc back into
            // plaintext Lucene files before the search runs.
            var search = new SearchService(indexPath, indexManager: null, MasterPassword, verifier);
            Assert.True(search.IndexExists());

            var results = search.Search("xilofoneabacaxi", topK: 5);
            Assert.Single(results);
            Assert.Contains("curriculo.txt", results[0].FileName);

            // ON CLOSE: sealing again must restore the encrypted-at-rest state.
            await search.SealAsync();
            Assert.Empty(Directory.GetFiles(indexPath, "segments_*"));
            Assert.True(File.Exists(Path.Combine(indexPath, LuceneIndexEncryptionService.EncryptedFileName)));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            if (Directory.Exists(indexPath)) Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task SearchService_WithWrongPassword_ThrowsInsteadOfSilentlyReturningEmptyResults()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"enc-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"enc-index-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "curriculo.txt"), "Conteudo protegido por senha mestre.");

            var verifier = MasterKeyManager.CreateVerifier(MasterPassword);
            var indexer = new DocumentIndexer();
            await indexer.IndexFolderAsync(folder, indexPath, masterPassword: MasterPassword, masterKeyVerifier: verifier);

            // A wrong password fails AES-GCM tag verification during decryption — this must
            // surface as a clear, catchable CryptographicException, not a crash or an empty result.
            Assert.ThrowsAny<CryptographicException>(() =>
                new SearchService(indexPath, indexManager: null, "senha-totalmente-errada", verifier));

            // The index.enc bundle must remain untouched/undamaged by the failed attempt.
            Assert.True(File.Exists(Path.Combine(indexPath, LuceneIndexEncryptionService.EncryptedFileName)));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            if (Directory.Exists(indexPath)) Directory.Delete(indexPath, recursive: true);
        }
    }
}
