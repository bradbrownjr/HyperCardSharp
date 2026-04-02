using HyperCardSharp.Core.Binary;
using HyperCardSharp.Core.Text;

namespace HyperCardSharp.Core.Stack;

/// <summary>
/// Parsed FTBL block. Maps font IDs to font names.
/// Layout (after 16-byte header):
/// +0x10: fontCount(4), +0x14: reserved(4),
/// +0x18: fontEntries[] (each: fontId(2) + null-terminated name + word-alignment padding)
/// </summary>
public class FontTableBlock
{
    public BlockHeader Header { get; init; }
    public int FontCount { get; init; }
    public List<FontEntry> Fonts { get; init; } = new();

    public string? GetFontName(int fontId)
        => Fonts.FirstOrDefault(f => f.FontId == fontId)?.Name;

    public static FontTableBlock Parse(ReadOnlySpan<byte> blockData, BlockHeader header)
    {
        var fontCount = BigEndianReader.ReadInt32At(blockData, 0x10);

        var fonts = new List<FontEntry>();
        int offset = 0x18;

        for (int i = 0; i < fontCount; i++)
        {
            if (offset + 3 > blockData.Length) // minimum: 2 bytes ID + 1 byte null
                break;

            var fontId = BigEndianReader.ReadUInt16At(blockData, offset);
            offset += 2;

            // Read null-terminated font name
            int nameStart = offset;
            while (offset < blockData.Length && blockData[offset] != 0)
                offset++;
            var name = MacRomanEncoding.GetString(blockData.Slice(nameStart, offset - nameStart));
            offset++; // skip null terminator

            // Word-align
            if (offset % 2 != 0)
                offset++;

            fonts.Add(new FontEntry { FontId = fontId, Name = name });
        }

        return new FontTableBlock
        {
            Header = header,
            FontCount = fontCount,
            Fonts = fonts
        };
    }
}

public class FontEntry
{
    public ushort FontId { get; init; }
    public string Name { get; init; } = "";
}
