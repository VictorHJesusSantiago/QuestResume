using System.Security.Cryptography;

namespace QuestResume.Core.Persistence;

public static class SecureWipeService
{
        public const int DefaultPasses = 3;

        public static void WipeFile(string filePath, int passes = DefaultPasses)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        var length = new FileInfo(filePath).Length;
        if (length > 0)
        {
            const int bufferSize = 64 * 1024;
            var buffer = new byte[bufferSize];

            for (var pass = 0; pass < Math.Max(1, passes); pass++)
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None);
                long written = 0;
                while (written < length)
                {
                    var chunk = (int)Math.Min(bufferSize, length - written);
                    RandomNumberGenerator.Fill(buffer.AsSpan(0, chunk));
                    stream.Write(buffer, 0, chunk);
                    written += chunk;
                }

                stream.Flush(flushToDisk: true);
            }
        }

        File.Delete(filePath);
    }

        public static int WipeDirectory(string directoryPath, int passes = DefaultPasses)
    {
        if (!Directory.Exists(directoryPath))
        {
            return 0;
        }

        var count = 0;
        foreach (var file in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
        {
            WipeFile(file, passes);
            count++;
        }

        Directory.Delete(directoryPath, recursive: true);
        return count;
    }
}
