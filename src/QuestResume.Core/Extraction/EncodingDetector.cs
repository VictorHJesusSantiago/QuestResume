using System.Text;

namespace QuestResume.Core.Extraction;

public static class EncodingDetector
{
        public static (Encoding Encoding, string Text) DetectAndDecode(byte[] bytes)
    {
        
        
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return (Encoding.Unicode, Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2));
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return (Encoding.BigEndianUnicode, Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2));
        }

        if (IsValidUtf8(bytes))
        {
            return (new UTF8Encoding(false), Encoding.UTF8.GetString(bytes));
        }

        
        
        
        
        var fallback = TryGetWindows1252() ?? Encoding.Latin1;
        return (fallback, fallback.GetString(bytes));
    }

        public static async Task<string> ReadAllTextDetectedAsync(string path, CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var (_, text) = DetectAndDecode(bytes);
        return text;
    }

    private static Encoding? TryGetWindows1252()
    {
        try
        {
            return Encoding.GetEncoding(1252);
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

        private static bool IsValidUtf8(byte[] bytes)
    {
        var i = 0;
        while (i < bytes.Length)
        {
            var b0 = bytes[i];

            if (b0 <= 0x7F)
            {
                i += 1;
                continue;
            }

            int extraBytes;
            int minCodePoint;
            int codePoint;

            if ((b0 & 0xE0) == 0xC0)
            {
                extraBytes = 1;
                minCodePoint = 0x80;
                codePoint = b0 & 0x1F;
            }
            else if ((b0 & 0xF0) == 0xE0)
            {
                extraBytes = 2;
                minCodePoint = 0x800;
                codePoint = b0 & 0x0F;
            }
            else if ((b0 & 0xF8) == 0xF0)
            {
                extraBytes = 3;
                minCodePoint = 0x10000;
                codePoint = b0 & 0x07;
            }
            else
            {
                return false;
            }

            if (i + extraBytes >= bytes.Length)
            {
                return false;
            }

            for (var j = 1; j <= extraBytes; j++)
            {
                var b = bytes[i + j];
                if ((b & 0xC0) != 0x80)
                {
                    return false;
                }

                codePoint = (codePoint << 6) | (b & 0x3F);
            }

            if (codePoint < minCodePoint || codePoint > 0x10FFFF || (codePoint >= 0xD800 && codePoint <= 0xDFFF))
            {
                
                
                return false;
            }

            i += extraBytes + 1;
        }

        return true;
    }
}
