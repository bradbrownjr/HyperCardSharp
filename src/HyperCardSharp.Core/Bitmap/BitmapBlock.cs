using HyperCardSharp.Core.Binary;

namespace HyperCardSharp.Core.Bitmap;

/// <summary>
/// Parsed BMAP block header. Contains card rect, mask rect, image rect,
/// and pointers to the compressed mask and image data.
/// Layout (HC 2.x, after 16-byte standard header):
/// +0x10: reserved(4), +0x14: reserved(4),
/// +0x18: cardRect(8), +0x20: maskRect(8), +0x28: imageRect(8),
/// +0x30: reserved(4), +0x34: reserved(4),
/// +0x38: maskDataSize(4), +0x3C: imageDataSize(4),
/// +0x40: maskData(maskDataSize), then imageData(imageDataSize)
/// </summary>
public class BitmapBlock
{
    public BlockHeader Header { get; init; }
    public MacRect CardRect { get; init; }
    public MacRect MaskRect { get; init; }
    public MacRect ImageRect { get; init; }
    public int MaskDataSize { get; init; }
    public int ImageDataSize { get; init; }
    public int MaskDataOffset { get; init; }
    public int ImageDataOffset { get; init; }

    public static BitmapBlock Parse(ReadOnlySpan<byte> blockData, BlockHeader header)
    {
        var cardRect = MacRect.ReadAt(blockData, 0x18);
        var maskRect = MacRect.ReadAt(blockData, 0x20);
        var imageRect = MacRect.ReadAt(blockData, 0x28);
        var maskDataSize = BigEndianReader.ReadInt32At(blockData, 0x38);
        var imageDataSize = BigEndianReader.ReadInt32At(blockData, 0x3C);

        return new BitmapBlock
        {
            Header = header,
            CardRect = cardRect,
            MaskRect = maskRect,
            ImageRect = imageRect,
            MaskDataSize = maskDataSize,
            ImageDataSize = imageDataSize,
            MaskDataOffset = 0x40,
            ImageDataOffset = 0x40 + maskDataSize
        };
    }
}

/// <summary>
/// Mac QuickDraw rectangle: top, left, bottom, right (all Int16, big-endian).
/// </summary>
public readonly record struct MacRect(short Top, short Left, short Bottom, short Right)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>32-bit aligned left boundary.</summary>
    public int AlignedLeft => Left & ~0x1F;

    /// <summary>32-bit aligned right boundary.</summary>
    public int AlignedRight => (Right & 0x1F) != 0 ? ((Right | 0x1F) + 1) : Right;

    /// <summary>Row width in bytes after 32-bit alignment.</summary>
    public int AlignedRowBytes => (AlignedRight - AlignedLeft) / 8;

    public static MacRect ReadAt(ReadOnlySpan<byte> data, int offset)
    {
        return new MacRect(
            BigEndianReader.ReadInt16At(data, offset),
            BigEndianReader.ReadInt16At(data, offset + 2),
            BigEndianReader.ReadInt16At(data, offset + 4),
            BigEndianReader.ReadInt16At(data, offset + 6)
        );
    }
}
