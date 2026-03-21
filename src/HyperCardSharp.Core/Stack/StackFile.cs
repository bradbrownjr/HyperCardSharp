using HyperCardSharp.Core.Binary;

namespace HyperCardSharp.Core.Stack;

/// <summary>
/// Top-level model representing a parsed HyperCard stack.
/// </summary>
public class StackFile
{
    public required StackBlock StackHeader { get; init; }
    public MasterBlock? MasterIndex { get; init; }
    public required IReadOnlyList<BlockHeader> Blocks { get; init; }
    public required ReadOnlyMemory<byte> RawData { get; init; }

    /// <summary>
    /// Get all blocks of a given type.
    /// </summary>
    public IEnumerable<BlockHeader> GetBlocks(string type)
        => Blocks.Where(b => b.Type == type);

    /// <summary>
    /// Get the raw data for a specific block.
    /// </summary>
    public ReadOnlySpan<byte> GetBlockData(BlockHeader header)
        => RawData.Span.Slice((int)header.FileOffset, header.Size);
}
