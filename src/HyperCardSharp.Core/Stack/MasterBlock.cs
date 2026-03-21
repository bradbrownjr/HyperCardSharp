using HyperCardSharp.Core.Binary;

namespace HyperCardSharp.Core.Stack;

/// <summary>
/// Parsed MAST block — the master index that maps block entries to file offsets.
///
/// After the 16-byte header, the MAST block contains successive 512-byte tables.
/// The first table entry (at +0x10) is reserved/unused.
/// Starting at +0x20, each 4-byte entry is the absolute file offset of a block.
/// Zero entries indicate unused slots.
/// </summary>
public class MasterBlock
{
    public BlockHeader Header { get; init; }

    /// <summary>
    /// Array of block offsets. Index = slot number, value = file offset.
    /// Zero means unused slot.
    /// </summary>
    public int[] Offsets { get; init; } = Array.Empty<int>();

    public static MasterBlock Parse(ReadOnlySpan<byte> blockData, BlockHeader header)
    {
        // The MAST block data starts after the 16-byte header.
        // Entries start at offset 0x20 within the block (skipping 16 bytes of header + 16 reserved).
        // Each entry is 4 bytes (big-endian int32 = file offset).
        int entryStart = 0x20;
        int entryCount = (header.Size - entryStart) / 4;

        var offsets = new int[entryCount];
        for (int i = 0; i < entryCount; i++)
        {
            offsets[i] = BigEndianReader.ReadInt32At(blockData, entryStart + i * 4);
        }

        return new MasterBlock
        {
            Header = header,
            Offsets = offsets
        };
    }
}
