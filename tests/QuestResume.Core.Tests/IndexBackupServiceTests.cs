using QuestResume.Core.Persistence;

namespace QuestResume.Core.Tests;

public class IndexBackupServiceTests
{
    [Fact]
    public async Task CreateAndRestoreBackup_RoundTripsAllFiles()
    {
        var indexPath = Path.Combine(Path.GetTempPath(), $"backup-index-{Guid.NewGuid()}");
        var restorePath = Path.Combine(Path.GetTempPath(), $"backup-restore-{Guid.NewGuid()}");
        var zipPath = Path.Combine(Path.GetTempPath(), $"backup-{Guid.NewGuid()}.zip");

        Directory.CreateDirectory(indexPath);
        Directory.CreateDirectory(Path.Combine(indexPath, "sub"));

        try
        {
            await File.WriteAllTextAsync(Path.Combine(indexPath, "tags.json"), "{\"documentTags\":{}}");
            await File.WriteAllTextAsync(Path.Combine(indexPath, "index-manifest.json"), "{\"files\":{}}");
            await File.WriteAllTextAsync(Path.Combine(indexPath, "sub", "segment.bin"), "conteúdo binário simulado");

            var backupService = new IndexBackupService();
            await backupService.CreateBackupAsync(indexPath, zipPath);

            Assert.True(File.Exists(zipPath));

            await backupService.RestoreBackupAsync(zipPath, restorePath);

            Assert.True(File.Exists(Path.Combine(restorePath, "tags.json")));
            Assert.True(File.Exists(Path.Combine(restorePath, "index-manifest.json")));
            Assert.True(File.Exists(Path.Combine(restorePath, "sub", "segment.bin")));

            var restoredContent = await File.ReadAllTextAsync(Path.Combine(restorePath, "sub", "segment.bin"));
            Assert.Equal("conteúdo binário simulado", restoredContent);
        }
        finally
        {
            if (Directory.Exists(indexPath)) Directory.Delete(indexPath, recursive: true);
            if (Directory.Exists(restorePath)) Directory.Delete(restorePath, recursive: true);
            if (File.Exists(zipPath)) File.Delete(zipPath);
        }
    }

    [Fact]
    public async Task RestoreBackup_ReplacesExistingContent()
    {
        var indexPath = Path.Combine(Path.GetTempPath(), $"backup-replace-{Guid.NewGuid()}");
        var zipPath = Path.Combine(Path.GetTempPath(), $"backup-replace-{Guid.NewGuid()}.zip");

        Directory.CreateDirectory(indexPath);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(indexPath, "original.txt"), "original");

            var backupService = new IndexBackupService();
            await backupService.CreateBackupAsync(indexPath, zipPath);

            // Simulate the index having changed since the backup was taken.
            File.Delete(Path.Combine(indexPath, "original.txt"));
            await File.WriteAllTextAsync(Path.Combine(indexPath, "stale.txt"), "deve ser removido");

            await backupService.RestoreBackupAsync(zipPath, indexPath);

            Assert.True(File.Exists(Path.Combine(indexPath, "original.txt")));
            Assert.False(File.Exists(Path.Combine(indexPath, "stale.txt")));
        }
        finally
        {
            if (Directory.Exists(indexPath)) Directory.Delete(indexPath, recursive: true);
            if (File.Exists(zipPath)) File.Delete(zipPath);
        }
    }

    [Fact]
    public async Task CreateBackupAsync_MissingIndexPath_Throws()
    {
        var indexPath = Path.Combine(Path.GetTempPath(), $"missing-index-{Guid.NewGuid()}");
        var zipPath = Path.Combine(Path.GetTempPath(), $"missing-backup-{Guid.NewGuid()}.zip");

        var backupService = new IndexBackupService();
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => backupService.CreateBackupAsync(indexPath, zipPath));
    }
}
