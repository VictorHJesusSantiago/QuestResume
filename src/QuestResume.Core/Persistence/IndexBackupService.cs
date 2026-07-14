using System.IO.Compression;
using IODirectory = System.IO.Directory;

namespace QuestResume.Core.Persistence;

/// <summary>
/// Backs up and restores an entire index folder (Lucene segments, <c>vectors.db</c>,
/// <c>tags.json</c>, <c>index-manifest.json</c>, <c>index-report.json</c>, <c>audit.jsonl</c>,
/// etc.) as a single .zip file, so a user can move or recover a full index without re-indexing
/// the original documents.
/// </summary>
public sealed class IndexBackupService
{
    /// <summary>
    /// Compacts every file directly under <paramref name="indexPath"/> into
    /// <paramref name="backupZipPath"/>. Any existing file at <paramref name="backupZipPath"/> is
    /// overwritten. Files still open for writing (e.g. <c>vectors.db-wal</c>) are copied with
    /// <see cref="FileShare.ReadWrite"/> so an in-progress SQLite connection doesn't block the backup.
    /// </summary>
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

        // Build into a temp file first and move into place at the end, so a crash or
        // cancellation mid-write never leaves a truncated/corrupt .zip at the requested path.
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

    /// <summary>
    /// Restores <paramref name="backupZipPath"/> into <paramref name="indexPath"/>, replacing its
    /// current contents. Extracts to a temporary sibling directory first and only swaps it into
    /// place once extraction succeeds fully, so a failed/cancelled restore never leaves
    /// <paramref name="indexPath"/> half-overwritten.
    /// </summary>
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
                        // Directory entry.
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

            // Atomic-ish swap: remove current index contents, then move every restored file in.
            // Mirrors DocumentIndexer.SwapIndexDirectory's crash-safety intent, but since restore
            // is an explicit, infrequent user action a marker file isn't warranted here — worst
            // case of an interruption is an empty/partial indexPath, recoverable by re-running
            // restore or re-indexing.
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
