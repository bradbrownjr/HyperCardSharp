using HyperCardSharp.Core.Binary;
using HyperCardSharp.Core.Parts;
using HyperCardSharp.Core.Text;

namespace HyperCardSharp.Core.Stack;

/// <summary>
/// Parsed BKGD block. Layout (HC 2.x, after 16-byte header):
/// +0x10: bitmapId(4), +0x14: flags(2), +0x16: reserved(2),
/// +0x18: cardCount(4), +0x1C: nextBackgroundId(4), +0x20: prevBackgroundId(4),
/// +0x24: partCount(2), +0x26: nextPartId(2), +0x28: partListSize(4),
/// +0x2C: partContentCount(2), +0x2E: partContentSize(4),
/// +0x32: parts[], then partContents[], then name\0, then script\0
/// </summary>
public class BackgroundBlock
{
    public BlockHeader Header { get; init; }
    public int BitmapId { get; init; }
    public ushort Flags { get; init; }
    public int CardCount { get; init; }
    public int NextBackgroundId { get; init; }
    public int PrevBackgroundId { get; init; }
    public List<Part> Parts { get; init; } = new();
    public List<PartContent> PartContents { get; init; } = new();
    public string Name { get; init; } = "";
    public string Script { get; init; } = "";

    public bool CantDelete => (Flags & 0x4000) != 0;
    public bool HideBackgroundPicture => (Flags & 0x2000) != 0;
    public bool DontSearch => (Flags & 0x0800) != 0;

    public static BackgroundBlock Parse(ReadOnlySpan<byte> blockData, BlockHeader header)
    {
        var bitmapId = BigEndianReader.ReadInt32At(blockData, 0x10);
        var flags = BigEndianReader.ReadUInt16At(blockData, 0x14);
        var cardCount = BigEndianReader.ReadInt32At(blockData, 0x18);
        var nextBgId = BigEndianReader.ReadInt32At(blockData, 0x1C);
        var prevBgId = BigEndianReader.ReadInt32At(blockData, 0x20);
        var partCount = BigEndianReader.ReadInt16At(blockData, 0x24);
        var partListSize = BigEndianReader.ReadInt32At(blockData, 0x28);
        var partContentCount = BigEndianReader.ReadInt16At(blockData, 0x2C);
        var partContentSize = BigEndianReader.ReadInt32At(blockData, 0x2E);

        var parts = new List<Part>();
        var partContents = new List<PartContent>();
        string name = "";
        string script = "";

        // Parts start at +0x32 (4 bytes earlier than CARD)
        int partsOffset = 0x32;
        if (partCount > 0 && partsOffset + partListSize <= blockData.Length)
        {
            parts = Part.ParseAll(blockData.Slice(partsOffset, partListSize), partCount);
        }

        int contentsOffset = partsOffset + partListSize;
        if (partContentCount > 0 && partContentSize > 0 && contentsOffset + partContentSize <= blockData.Length)
        {
            partContents = PartContent.ParseAll(blockData.Slice(contentsOffset, partContentSize), partContentCount);
        }

        int nameOffset = contentsOffset + partContentSize;
        if (nameOffset < blockData.Length)
        {
            name = ReadNullTerminatedString(blockData, nameOffset);
            int scriptOffset = nameOffset + name.Length + 1;
            if (scriptOffset < blockData.Length)
                script = ReadNullTerminatedString(blockData, scriptOffset);
        }

        return new BackgroundBlock
        {
            Header = header,
            BitmapId = bitmapId,
            Flags = flags,
            CardCount = cardCount,
            NextBackgroundId = nextBgId,
            PrevBackgroundId = prevBgId,
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
