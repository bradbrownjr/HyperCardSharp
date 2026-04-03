namespace HyperCardSharp.Core.Text;

/// <summary>
/// Converts Mac OS Roman (code page 10000) byte sequences to .NET strings.
/// Bytes 0x00–0x7F are identical to ASCII/Latin-1; bytes 0x80–0xFF diverge.
/// Mapping sourced from the Unicode Consortium's ROMAN.TXT (https://www.unicode.org/Public/MAPPINGS/VENDORS/APPLE/ROMAN.TXT).
/// </summary>
public static class MacRomanEncoding
{
    /// <summary>
    /// Unicode code points for Mac Roman bytes 0x80–0xFF.
    /// Index 0 in this array corresponds to byte 0x80, index 127 to byte 0xFF.
    /// </summary>
    private static readonly char[] s_highTable =
    {
        // 0x80–0x8F
        '\u00C4', '\u00C5', '\u00C7', '\u00C9', '\u00D1', '\u00D6', '\u00DC', '\u00E1',
        '\u00E0', '\u00E2', '\u00E4', '\u00E3', '\u00E5', '\u00E7', '\u00E9', '\u00E8',
        // 0x90–0x9F
        '\u00EA', '\u00EB', '\u00ED', '\u00EC', '\u00EE', '\u00EF', '\u00F1', '\u00F3',
        '\u00F2', '\u00F4', '\u00F6', '\u00FA', '\u00F9', '\u00FB', '\u00FC', '\u2020',
        // 0xA0–0xAF
        '\u00B0', '\u00A2', '\u00A3', '\u00A7', '\u2022', '\u00B6', '\u00DF', '\u00AE',
        '\u00A9', '\u2122', '\u00B4', '\u00A8', '\u2260', '\u00C6', '\u00D8', '\u221E',
        // 0xB0–0xBF
        '\u00B1', '\u2264', '\u2265', '\u00A5', '\u00B5', '\u2202', '\u03A3', '\u03A0',
        '\u03C0', '\u222B', '\u00AA', '\u00BA', '\u03A9', '\u00E6', '\u00F8', '\u00BF',
        // 0xC0–0xCF
        '\u00A1', '\u00AC', '\u221A', '\u0192', '\u2248', '\u0394', '\u00AB', '\u00BB',
        '\u2026', '\u00A0', '\u00C0', '\u00C3', '\u00D5', '\u0152', '\u0153', '\u2013',
        // 0xD0–0xDF
        // 0xD0=en dash, 0xD1=em dash, 0xD2–0xD3=typographic double quotes,
        // 0xD4–0xD5=typographic single quotes (0xD5=RIGHT = apostrophe in HyperCard text).
        // Previously this row was missing U+2013 at 0xD0, shifting every subsequent
        // character one byte too early and mapping 0xD5 → ÷ instead of '.
        '\u2013', '\u2014', '\u201C', '\u201D', '\u2018', '\u2019', '\u00F7', '\u25CA',
        '\u00FF', '\u0178', '\u2044', '\u20AC', '\u2039', '\u203A', '\uFB01', '\uFB02',
        // 0xE0–0xEF
        '\u00B7', '\u201A', '\u201E', '\u2030', '\u00C2', '\u00CA', '\u00C1', '\u00CB',
        '\u00C8', '\u00CD', '\u00CE', '\u00CF', '\u00CC', '\u00D3', '\u00D4', '\uF8FF',
        // 0xF0–0xFF
        '\u00D2', '\u00DA', '\u00DB', '\u00D9', '\u0131', '\u02C6', '\u02DC', '\u00AF',
        '\u02D8', '\u02D9', '\u02DA', '\u00B8', '\u02DD', '\u02DB', '\u02C7', '\u02D9',
    };

    /// <summary>
    /// Decodes a Mac Roman byte span to a .NET string.
    /// Stops at the first null byte if present.
    /// </summary>
    public static string GetString(ReadOnlySpan<byte> data)
    {
        // Fast path: no bytes above 0x7F and no null terminator — use ASCII
        int len = data.Length;
        // Trim null terminator
        for (int i = 0; i < len; i++)
            if (data[i] == 0) { len = i; break; }

        if (len == 0) return string.Empty;

        var chars = new char[len];
        for (int i = 0; i < len; i++)
        {
            byte b = data[i];
            chars[i] = b < 0x80 ? (char)b : s_highTable[b - 0x80];
        }
        return new string(chars);
    }

    /// <summary>
    /// Decodes a slice of a Mac Roman byte array to a .NET string.
    /// </summary>
    public static string GetString(byte[] data, int offset, int length)
        => GetString(data.AsSpan(offset, length));

    /// <summary>
    /// Decodes an entire Mac Roman byte array to a .NET string.
    /// </summary>
    public static string GetString(byte[] data)
        => GetString(data.AsSpan());
}
