using System.Text;

namespace QuestResume.Core.Extraction;

/// <summary>
/// Lightweight heuristic encoding detection for plain-text files (item 9): checks for a BOM
/// first; if absent, tries to decode the raw bytes as strict UTF-8 (rejecting malformed/invalid
/// byte sequences) and falls back to Windows-1252/Latin-1 when that fails. Not a full charset
/// detector (no statistical language models) — just enough to avoid silently corrupting Latin1/
/// CP1252 files (common on Windows/legacy exports) that don't happen to be valid UTF-8.
/// </summary>
public static class EncodingDetector
{
    /// <summary>
    /// Detects the encoding of <paramref name="bytes"/> and returns the decoded text plus the
    /// encoding used. BOM (UTF-8/UTF-16 LE/BE) takes precedence; otherwise strict UTF-8 is tried
    /// first (most text today is UTF-8), falling back to Windows-1252 (superset of Latin-1,
    /// covers "é" = 0xE9 etc.) when the bytes aren't valid UTF-8.
    /// </summary>
    public static (Encoding Encoding, string Text) DetectAndDecode(byte[] bytes)
    {
        // BOM detection (UTF-8, UTF-16 LE/BE) — handled by StreamReader's own logic when reading
        // from a file, but done explicitly here since callers may already have the raw bytes.
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

        // Windows-1252 is available built-in as a distinct code page on modern .NET (via the
        // system's code page tables) on Windows; Encoding.Latin1 (ISO-8859-1, built-in, no extra
        // package needed) is used as a portable fallback that still maps single bytes 1:1 to the
        // same code points for the accented-letter range that matters here (0xE9 "é" etc.).
        var fallback = TryGetWindows1252() ?? Encoding.Latin1;
        return (fallback, fallback.GetString(bytes));
    }

    /// <summary>Reads a file and decodes it using <see cref="DetectAndDecode"/>.</summary>
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

    /// <summary>
    /// Manually validates that <paramref name="bytes"/> is a well-formed UTF-8 byte sequence
    /// (proper continuation-byte counts, no overlong encodings, no surrogate halves, within the
    /// valid Unicode range), without relying on <see cref="Encoding.UTF8"/>'s decoder (which by
    /// default silently replaces invalid bytes with U+FFFD instead of failing).
    /// </summary>
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
                // Overlong encoding, out-of-range code point, or an encoded surrogate half
                // (invalid in UTF-8 — surrogates only exist in UTF-16).
                return false;
            }

            i += extraBytes + 1;
        }

        return true;
    }
}
