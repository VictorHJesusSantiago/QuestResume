using System.IO.Compression;
using IODirectory = System.IO.Directory;

namespace QuestResume.Core.Persistence;

public sealed class IndexBackupService
{
        public async Task CreateBackupAsync(string indexPath, string backupZipPath, CancellationToken cancellationToken = default)
    {
        if (!IODirectory.Exists(indexPath))
        {
            throw new DirectoryNotFoundException($"Pasta de índice não encontrada: {indexPath}");
        }

        var destinationDir = Path.GetDirectoryName(Path.GetFullPath(backupZipPath));
        if (!string.IsNullOrEmpty(destinationDir))
        {
            IODirectory.CreateDirectory(destinationDir);
        }

        
        
        var tempZipPath = backupZipPath + ".tmp";
        if (File.Exists(tempZipPath))
        {
            File.Delete(tempZipPath);
        }

        try
        {
            using (var zipStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                foreach (var filePath in IODirectory.EnumerateFiles(indexPath, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var entryName = Path.GetRelativePath(indexPath, filePath).Replace('\\', '/');
                    var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);

                    using var entryStream = entry.Open();
                    using var sourceStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    await sourceStream.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);
                }
            }

            if (File.Exists(backupZipPath))
            {
                File.Delete(backupZipPath);
            }

            File.Move(tempZipPath, backupZipPath);
        }
        finally
        {
            if (File.Exists(tempZipPath))
            {
                File.Delete(tempZipPath);
            }
        }
    }

        public async Task RestoreBackupAsync(string backupZipPath, string indexPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(backupZipPath))
        {
            throw new FileNotFoundException($"Arquivo de backup não encontrado: {backupZipPath}", backupZipPath);
        }

        IODirectory.CreateDirectory(indexPath);

        var parentDir = IODirectory.GetParent(Path.GetFullPath(indexPath))?.FullName
            ?? Path.GetTempPath();
        var tempRestoreDir = Path.Combine(parentDir, $"_restore_tmp_{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(tempRestoreDir);

        try
        {
            using (var zipStream = new FileStream(backupZipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
            {
                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        
                        continue;
                    }

                    var destinationPath = Path.Combine(tempRestoreDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                    var destinationDir = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(destinationDir))
                    {
                        IODirectory.CreateDirectory(destinationDir);
                    }

                    using var entryStream = entry.Open();
                    using var destinationStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await entryStream.CopyToAsync(destinationStream, cancellationToken).ConfigureAwait(false);
                }
            }

            
            
            
            
            
            foreach (var existingFile in IODirectory.GetFiles(indexPath, "*", SearchOption.AllDirectories))
            {
                File.Delete(existingFile);
            }

            foreach (var existingDir in IODirectory.GetDirectories(indexPath))
            {
                IODirectory.Delete(existingDir, recursive: true);
            }

            foreach (var restoredFile in IODirectory.EnumerateFiles(tempRestoreDir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(tempRestoreDir, restoredFile);
                var destination = Path.Combine(indexPath, relative);
                var destinationDir = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(destinationDir))
                {
                    IODirectory.CreateDirectory(destinationDir);
                }

                File.Move(restoredFile, destination, overwrite: true);
            }
        }
        finally
        {
            if (IODirectory.Exists(tempRestoreDir))
            {
                IODirectory.Delete(tempRestoreDir, recursive: true);
            }
        }
    }
}
