using HyperCardSharp.Core.Binary;
using HyperCardSharp.Core.Bitmap;
using HyperCardSharp.Core.Resources;

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
    /// ICON resources keyed by resource ID (short).
    /// Each value is the raw 128-byte 1-bit 32×32 bitmap data as stored in the Mac resource fork.
    /// Populated when the stack was opened from a container that provided a resource fork.
    /// </summary>
    public Dictionary<short, byte[]> Icons { get; init; } = new();

    /// <summary>
    /// 'snd ' resources keyed by resource ID.
    /// Raw resource bytes; use <see cref="HyperCardSharp.Core.Resources.SoundDecoder"/> to convert to WAV.
    /// </summary>
    public Dictionary<short, byte[]> SoundsById { get; init; } = new();

    /// <summary>
    /// 'snd ' resources keyed by resource name (case-insensitive).
    /// Populated only for resources that have a name in the resource fork name list.
    /// </summary>
    public Dictionary<string, byte[]> SoundsByName { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// HCcd (card-level AddColor) resources keyed by card block ID.
    /// Each value is a list of colour regions that should be composited under the card BMAP
    /// when rendering in Colour mode.
    /// </summary>
    public Dictionary<int, IReadOnlyList<AddColorDecoder.ColorRegion>> CardColorData { get; init; } = new();

    /// <summary>
    /// HCbg (background-level AddColor) resources keyed by background block ID.
    /// </summary>
    public Dictionary<int, IReadOnlyList<AddColorDecoder.ColorRegion>> BackgroundColorData { get; init; } = new();

    /// <summary>
    /// 'PICT' resources keyed by resource ID.
    /// Raw PICT data; use <see cref="HyperCardSharp.Rendering.PictDecoder"/> to decode to bitmap.
    /// </summary>
    public Dictionary<short, byte[]> PictResources { get; init; } = new();

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
