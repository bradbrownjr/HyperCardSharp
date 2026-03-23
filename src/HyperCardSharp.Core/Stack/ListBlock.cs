using HyperCardSharp.Core.Binary;

namespace HyperCardSharp.Core.Stack;

/// <summary>
/// Parsed LIST block. Contains page references and card ordering metadata.
/// Layout (after 16-byte header):
/// +0x10: pageCount(4), +0x14: pageSize(4), +0x18: totalCardCount(4),
/// +0x1C: cardReferenceSize(2), +0x1E: reserved(2),
/// +0x20: hashIntegerCount(2), +0x22: searchHashCount(2),
/// +0x24: checksum(4), +0x28: totalCardCount2(4), +0x2C: reserved(4),
/// +0x30: pageReferences[] (6 bytes each: pageBlockId(4) + cardCount(2))
/// </summary>
public class ListBlock
{
    public BlockHeader Header { get; init; }
    public int PageCount { get; init; }
    public int PageSize { get; init; }
    public int TotalCardCount { get; init; }
    public ushort CardReferenceSize { get; init; }
    public List<PageReference> PageReferences { get; init; } = new();

    public static ListBlock Parse(ReadOnlySpan<byte> blockData, BlockHeader header)
    {
        var pageCount = BigEndianReader.ReadInt32At(blockData, 0x10);
        var pageSize = BigEndianReader.ReadInt32At(blockData, 0x14);
        var totalCardCount = BigEndianReader.ReadInt32At(blockData, 0x18);
        var cardRefSize = BigEndianReader.ReadUInt16At(blockData, 0x1C);

        var pageRefs = new List<PageReference>();
        int offset = 0x30;
        for (int i = 0; i < pageCount; i++)
        {
            if (offset + 6 > blockData.Length)
                break;
            var pageBlockId = BigEndianReader.ReadInt32At(blockData, offset);
            var cardCount = BigEndianReader.ReadUInt16At(blockData, offset + 4);
            pageRefs.Add(new PageReference { PageBlockId = pageBlockId, CardCount = cardCount });
            offset += 6;
        }

        return new ListBlock
        {
            Header = header,
            PageCount = pageCount,
            PageSize = pageSize,
            TotalCardCount = totalCardCount,
            CardReferenceSize = cardRefSize,
            PageReferences = pageRefs
        };
    }
}

public class PageReference
{
    public int PageBlockId { get; init; }
    public ushort CardCount { get; init; }
}
