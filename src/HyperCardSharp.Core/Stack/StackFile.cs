using HyperCardSharp.Core.Binary;
using HyperCardSharp.Core.Bitmap;

namespace HyperCardSharp.Core.Stack;

/// <summary>
/// Top-level model representing a parsed HyperCard stack.
/// </summary>
public class StackFile
{
    public required StackBlock StackHeader { get; init; }
    public MasterBlock? MasterIndex { get; init; }
    public ListBlock? ListIndex { get; init; }
    public FontTableBlock? FontTable { get; init; }
    public StyleTableBlock? StyleTable { get; init; }
    public required IReadOnlyList<BlockHeader> Blocks { get; init; }
    public required ReadOnlyMemory<byte> RawData { get; init; }

    public List<CardBlock> Cards { get; init; } = new();
    public List<BackgroundBlock> Backgrounds { get; init; } = new();
    public List<PageBlock> Pages { get; init; } = new();
    public Dictionary<int, BitmapBlock> Bitmaps { get; init; } = new();

    /// <summary>
    /// Get the ordered list of card IDs from PAGE blocks.
    /// </summary>
    public IEnumerable<int> GetCardOrder()
        => Pages.SelectMany(p => p.CardReferences.Select(r => r.CardId));

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
