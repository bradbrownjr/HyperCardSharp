using HyperCardSharp.Core.Binary;
using HyperCardSharp.Core.Parts;
using HyperCardSharp.Core.Text;

namespace HyperCardSharp.Core.Stack;

/// <summary>
/// Parsed CARD block. Layout (HC 2.x, after 16-byte header):
/// +0x10: bitmapId(4), +0x14: flags(2), +0x16: reserved(2),
/// +0x18: reserved(4), +0x1C: reserved(4),
/// +0x20: pageBlockId(4), +0x24: backgroundId(4),
/// +0x28: partCount(2), +0x2A: nextPartId(2), +0x2C: partListSize(4),
/// +0x30: partContentCount(2), +0x32: partContentSize(4),
/// +0x36: parts[], then partContents[], then name\0, then script\0
/// </summary>
public class CardBlock
{
    public BlockHeader Header { get; init; }
    public int BitmapId { get; init; }
    public ushort Flags { get; init; }
    public int PageBlockId { get; init; }
    public int BackgroundId { get; init; }
    public List<Part> Parts { get; init; } = new();
    public List<PartContent> PartContents { get; init; } = new();
    public string Name { get; init; } = "";
    public string Script { get; init; } = "";

    public bool CantDelete => (Flags & 0x4000) != 0;
    public bool HideCardPicture => (Flags & 0x2000) != 0;
    public bool DontSearch => (Flags & 0x0800) != 0;

    public static CardBlock Parse(ReadOnlySpan<byte> blockData, BlockHeader header)
    {
        var bitmapId = BigEndianReader.ReadInt32At(blockData, 0x10);
        var flags = BigEndianReader.ReadUInt16At(blockData, 0x14);
        var pageBlockId = BigEndianReader.ReadInt32At(blockData, 0x20);
        var backgroundId = BigEndianReader.ReadInt32At(blockData, 0x24);
        var partCount = BigEndianReader.ReadInt16At(blockData, 0x28);
        var partListSize = BigEndianReader.ReadInt32At(blockData, 0x2C);
        var partContentCount = BigEndianReader.ReadInt16At(blockData, 0x30);
        var partContentSize = BigEndianReader.ReadInt32At(blockData, 0x32);

        // Parse parts starting at +0x36
        var parts = new List<Part>();
        var partContents = new List<PartContent>();
        string name = "";
        string script = "";

        int partsOffset = 0x36;
        if (partCount > 0 && partsOffset + partListSize <= blockData.Length)
        {
            parts = Part.ParseAll(blockData.Slice(partsOffset, partListSize), partCount);
        }

        // Parse part contents after the parts list
        int contentsOffset = partsOffset + partListSize;
        if (partContentCount > 0 && partContentSize > 0 && contentsOffset + partContentSize <= blockData.Length)
        {
            partContents = PartContent.ParseAll(blockData.Slice(contentsOffset, partContentSize), partContentCount);
        }

        // Name and script follow after part contents
        int nameOffset = contentsOffset + partContentSize;
        if (nameOffset < blockData.Length)
        {
            name = ReadNullTerminatedString(blockData, nameOffset);
            int scriptOffset = nameOffset + name.Length + 1;
            if (scriptOffset < blockData.Length)
                script = ReadNullTerminatedString(blockData, scriptOffset);
        }

        return new CardBlock
        {
            Header = header,
            BitmapId = bitmapId,
            Flags = flags,
            PageBlockId = pageBlockId,
            BackgroundId = backgroundId,
            Parts = parts,
            PartContents = partContents,
            Name = name,
            Script = script
        };
    }

    private static string ReadNullTerminatedString(ReadOnlySpan<byte> data, int offset)
    {
        int end = offset;
        while (end < data.Length && data[end] != 0)
            end++;
        if (end == offset) return "";
        return MacRomanEncoding.GetString(data.Slice(offset, end - offset));
    }
}
