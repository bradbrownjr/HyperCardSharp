using HyperCardSharp.Core.Binary;

namespace HyperCardSharp.Core.Stack;

/// <summary>
/// Parsed PAGE block. Contains card reference entries for card ordering.
/// Layout (after 16-byte header):
/// +0x10: listBlockId(4), +0x14: checksum(4),
/// +0x18: cardReferences[] (each cardReferenceSize bytes from LIST block)
/// </summary>
public class PageBlock
{
    public BlockHeader Header { get; init; }
    public int ListBlockId { get; init; }
    public int Checksum { get; init; }
    public List<CardReference> CardReferences { get; init; } = new();

    /// <summary>
    /// Parse a PAGE block. Requires cardReferenceSize from the parent LIST block.
    /// </summary>
    public static PageBlock Parse(ReadOnlySpan<byte> blockData, BlockHeader header,
        ushort cardReferenceSize, int expectedCardCount)
    {
        var listBlockId = BigEndianReader.ReadInt32At(blockData, 0x10);
        var checksum = BigEndianReader.ReadInt32At(blockData, 0x14);

        var cardRefs = new List<CardReference>();
        int offset = 0x18;
        for (int i = 0; i < expectedCardCount; i++)
        {
            if (offset + 5 > blockData.Length)
                break;

            var cardId = BigEndianReader.ReadInt32At(blockData, offset);
            var flags = blockData[offset + 4];
            cardRefs.Add(new CardReference { CardId = cardId, Flags = flags });
            offset += cardReferenceSize;
        }

        return new PageBlock
        {
            Header = header,
            ListBlockId = listBlockId,
            Checksum = checksum,
            CardReferences = cardRefs
        };
    }
}

public class CardReference
{
    public int CardId { get; init; }
    public byte Flags { get; init; }

    public bool IsMarked => (Flags & 0x10) != 0;
    public bool HasTextContent => (Flags & 0x20) != 0;
    public bool IsBackgroundStart => (Flags & 0x40) != 0;
    public bool HasName => (Flags & 0x80) != 0;
}
