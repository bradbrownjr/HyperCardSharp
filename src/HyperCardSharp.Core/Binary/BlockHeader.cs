namespace HyperCardSharp.Core.Binary;

/// <summary>
/// The 16-byte header present at the start of every HyperCard block.
/// </summary>
public readonly record struct BlockHeader
{
    /// <summary>Block size in bytes (includes this header).</summary>
    public int Size { get; init; }

    /// <summary>4-character block type (STAK, MAST, LIST, PAGE, CARD, BKGD, BMAP, STBL, FTBL, FREE, TAIL, etc.).</summary>
    public string Type { get; init; }

    /// <summary>Block ID. -1 for STAK, MAST, LIST, TAIL. Sequential IDs for CARD, BKGD, PAGE, BMAP.</summary>
    public int Id { get; init; }

    /// <summary>Absolute byte offset of this block within the stack file.</summary>
    public long FileOffset { get; init; }

    /// <summary>
    /// Parse a block header from the given span (must be at least 16 bytes).
    /// </summary>
    public static BlockHeader Parse(ReadOnlySpan<byte> data, long fileOffset)
    {
        var reader = new BigEndianReader(data);
        return new BlockHeader
        {
            Size = reader.ReadInt32(),
            Type = reader.ReadAscii(4),
            Id = reader.ReadInt32(),
            FileOffset = fileOffset
        };
        // Bytes 12-15 are filler (always 0), skipped.
    }

    public override string ToString() => $"{Type} id={Id} size={Size} offset=0x{FileOffset:X}";
}
