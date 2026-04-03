using HyperCardSharp.Core.Binary;
using HyperCardSharp.Core.Text;

namespace HyperCardSharp.Core.Parts;

/// <summary>
/// Text content for a part (button or field), possibly with style runs.
/// </summary>
public class PartContent
{
    public short PartId { get; init; }
    /// <summary>Field/button text content. Settable so HyperTalk can mutate it at runtime.</summary>
    public string Text { get; set; } = "";
    public List<StyleRun> StyleRuns { get; init; } = new();
    public bool HasStyles => StyleRuns.Count > 0;

    /// <summary>
    /// Total bytes consumed by this entry (including alignment padding).
    /// </summary>
    public int TotalSize { get; init; }

    /// <summary>
    /// Parse all part content entries from the content region of a CARD/BKGD block.
    /// </summary>
    public static List<PartContent> ParseAll(ReadOnlySpan<byte> contentData, int contentCount)
    {
        var contents = new List<PartContent>(contentCount);
        int offset = 0;

        for (int i = 0; i < contentCount; i++)
        {
            if (offset + 4 > contentData.Length)
                break;

            var entry = ParseOne(contentData.Slice(offset));
            contents.Add(entry);
            offset += entry.TotalSize;
        }

        return contents;
    }

    private static PartContent ParseOne(ReadOnlySpan<byte> data)
    {
        var partId = BigEndianReader.ReadInt16At(data, 0);
        var contentLength = (int)BigEndianReader.ReadUInt16At(data, 2);

        string text = "";
        var styleRuns = new List<StyleRun>();
        int headerSize = 4;

        if (contentLength > 0)
        {
            var contentData = data.Slice(headerSize, contentLength);

            // Check if styled: first 2 bytes form a UInt16 with bit 15 set
            if (contentLength >= 2)
            {
                var marker = BigEndianReader.ReadUInt16At(contentData, 0);
                if ((marker & 0x8000) != 0)
                {
                    // Styled text
                    int formattingSize = marker & 0x7FFF;
                    int runCount = (formattingSize - 2) / 4;

                    for (int r = 0; r < runCount; r++)
                    {
                        int runOffset = 2 + r * 4;
                        if (runOffset + 4 > contentData.Length)
                            break;
                        var charPos = BigEndianReader.ReadUInt16At(contentData, runOffset);
                        var styleId = BigEndianReader.ReadUInt16At(contentData, runOffset + 2);
                        styleRuns.Add(new StyleRun { CharacterPosition = charPos, StyleId = styleId });
                    }

                    int textStart = formattingSize;
                    if (textStart < contentLength)
                        text = ReadMacRoman(contentData.Slice(textStart));
                }
                else
                {
                    // Plain text — skip first 0x00 byte marker
                    if (contentData[0] == 0x00 && contentLength > 1)
                        text = ReadMacRoman(contentData.Slice(1));
                    else
                        text = ReadMacRoman(contentData);
                }
            }
        }

        // Entries are word-aligned (padded to 2-byte boundary)
        int rawSize = headerSize + contentLength;
        int totalSize = (rawSize + 1) & ~1; // round up to even

        return new PartContent
        {
            PartId = partId,
            Text = text,
            StyleRuns = styleRuns,
            TotalSize = totalSize
        };
    }

    private static string ReadMacRoman(ReadOnlySpan<byte> data)
    {
        // HyperCard appends 0xCA as an internal record-termination byte at the end of
        // styled and plain text content. In Mac Roman 0xCA maps to U+00C0 (À), but
        // HyperCard's text engine never rendered it — it is structural punctuation.
        // Strip any trailing 0xCA bytes before converting to a .NET string.
        int len = data.Length;
        while (len > 0 && data[len - 1] == 0xCA) len--;
        return MacRomanEncoding.GetString(data.Slice(0, len));
    }
}

public class StyleRun
{
    public ushort CharacterPosition { get; init; }
    public ushort StyleId { get; init; }
}
