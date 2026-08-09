using System.IO.Compression;
using QuestResume.Core.Security;

namespace QuestResume.Core.Indexing;

public static class LuceneIndexEncryptionService
{
    public const string EncryptedFileName = "index.enc";

        public static bool OpenIntoWorkingFolder(string indexPath, string workingPath, string masterPassword, byte[] salt)
    {
        var encPath = Path.Combine(indexPath, EncryptedFileName);
        if (!File.Exists(encPath))
        {
            return false;
        }

        
        
        
        if (Directory.Exists(workingPath) && Directory.GetFiles(workingPath, "segments_*").Length > 0)
        {
            return true;
        }

        Directory.CreateDirectory(workingPath);
        var key = MasterKeyManager.DeriveKey(masterPassword, salt);
        var zipBytes = AesCryptoHelper.Decrypt(File.ReadAllBytes(encPath), key);

        var tempZip = Path.Combine(Path.GetTempPath(), $"questresume_idx_{Guid.NewGuid():N}.zip");
        try
        {
            File.WriteAllBytes(tempZip, zipBytes);
            ZipFile.ExtractToDirectory(tempZip, workingPath, overwriteFiles: true);
        }
        finally
        {
            if (File.Exists(tempZip))
            {
                File.Delete(tempZip);
            }
        }

        return true;
    }

        public static void SealFromWorkingFolder(string indexPath, string workingPath, string masterPassword, byte[] salt)
    {
        if (!Directory.Exists(workingPath))
        {
            return;
        }

        Directory.CreateDirectory(indexPath);
        var key = MasterKeyManager.DeriveKey(masterPassword, salt);
        var encPath = Path.Combine(indexPath, EncryptedFileName);

        
        
        
        if (File.Exists(encPath))
        {
            File.Delete(encPath);
        }

        var tempZip = Path.Combine(Path.GetTempPath(), $"questresume_idx_{Guid.NewGuid():N}.zip");
        try
        {
            if (File.Exists(tempZip))
            {
                File.Delete(tempZip);
            }

            ZipFile.CreateFromDirectory(workingPath, tempZip, CompressionLevel.Fastest, includeBaseDirectory: false);
            var zipBytes = File.ReadAllBytes(tempZip);
            var encrypted = AesCryptoHelper.Encrypt(zipBytes, key);
            File.WriteAllBytes(encPath, encrypted);
        }
        finally
        {
            if (File.Exists(tempZip))
            {
                File.Delete(tempZip);
            }
        }

        
        
        
        foreach (var file in Directory.GetFiles(workingPath))
        {
            if (string.Equals(Path.GetFileName(file), EncryptedFileName, StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                
                
                
            }
        }

        foreach (var dir in Directory.GetDirectories(workingPath))
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                
            }
        }
    }

        public static Task<bool> OpenAsync(string indexPath, string workingPath, string masterPassword, byte[] salt)
        => Task.FromResult(OpenIntoWorkingFolder(indexPath, workingPath, masterPassword, salt));

        public static Task SealAsync(string indexPath, string workingPath, string masterPassword, byte[] salt)
    {
        SealFromWorkingFolder(indexPath, workingPath, masterPassword, salt);
        return Task.CompletedTask;
    }
}
