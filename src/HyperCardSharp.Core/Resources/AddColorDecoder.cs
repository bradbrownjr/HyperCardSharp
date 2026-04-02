using System.Buffers.Binary;

namespace HyperCardSharp.Core.Resources;

/// <summary>
/// Decodes AddColor (HCcd/HCbg) resources from HyperCard 2.x stacks.
/// AddColor was an XCMD extension that allowed color overlays to be stored in the
/// resource fork. Each card gets an HCcd resource (ID = card block ID) and each
/// background gets an HCbg resource (ID = background block ID).
///
/// Format reference: https://hypercard.org/addcolor_resource_format/
///
/// Format (tentative — TODO: verify against real stacks with AddColor):
///   +0   int16  count            (number of color entries)
///   Per entry (22 bytes):
///     +0   int16  partId          (0 = card background area, >0 = part ID)
///     +2   int16  rectTop
///     +4   int16  rectLeft
///     +6   int16  rectBottom
///     +8   int16  rectRight
///     +10  uint16 fillRed         (QuickDraw RGBColor, 0–65535)
///     +12  uint16 fillGreen
///     +14  uint16 fillBlue
///     +16  uint16 frameRed
///     +18  uint16 frameGreen
///     +20  uint16 frameBlue
/// </summary>
public static class AddColorDecoder
{
    private const int RecordSize = 22;

    /// <summary>
    /// Represents a single color region from an HCcd or HCbg resource.
    /// </summary>
    public class ColorRegion
    {
        /// <summary>
        /// Part ID this region targets.  0 = the card/background bitmap area itself.
        /// Positive values match a card or background part ID.
        /// </summary>
        public short PartId { get; init; }

        public short Top    { get; init; }
        public short Left   { get; init; }
        public short Bottom { get; init; }
        public short Right  { get; init; }

        /// <summary>Fill color as ARGB packed 0xAARRGGBB (alpha = 0xFF).</summary>
        public uint FillColor { get; init; }

        /// <summary>Frame (border) color as ARGB packed 0xAARRGGBB (alpha = 0xFF).</summary>
        public uint FrameColor { get; init; }
    }

    /// <summary>
    /// Attempts to decode an HCcd or HCbg resource blob.
    /// Returns an empty list if the format is unrecognized or not yet implemented.
    /// </summary>
    public static IReadOnlyList<ColorRegion> Decode(byte[] resourceData)
    {
        if (resourceData == null || resourceData.Length < 2)
            return Array.Empty<ColorRegion>();

        try
        {
            return DecodeCore(resourceData.AsSpan());
        }
        catch
        {
            return Array.Empty<ColorRegion>();
        }
    }

    private static IReadOnlyList<ColorRegion> DecodeCore(ReadOnlySpan<byte> span)
    {
        short count = BinaryPrimitives.ReadInt16BigEndian(span.Slice(0, 2));
        if (count <= 0 || count > 1024)
            return Array.Empty<ColorRegion>();

        // Validate that the resource data is consistent with the expected record size.
        // Some stacks may omit the frame color (14-byte records); detect and accommodate.
        int dataLen   = span.Length - 2;
        int recSize   = (dataLen == count * 14) ? 14 : RecordSize;
        int minNeeded = 2 + count * recSize;
        if (span.Length < minNeeded)
            return Array.Empty<ColorRegion>();

        var result = new List<ColorRegion>(count);

        for (int i = 0; i < count; i++)
        {
            int off = 2 + i * recSize;
            short partId = BinaryPrimitives.ReadInt16BigEndian(span.Slice(off + 0, 2));
            short top    = BinaryPrimitives.ReadInt16BigEndian(span.Slice(off + 2, 2));
            short left   = BinaryPrimitives.ReadInt16BigEndian(span.Slice(off + 4, 2));
            short bottom = BinaryPrimitives.ReadInt16BigEndian(span.Slice(off + 6, 2));
            short right  = BinaryPrimitives.ReadInt16BigEndian(span.Slice(off + 8, 2));

            // Convert QuickDraw RGBColor (0–65535 per channel) to 8-bit (drop low byte)
            byte fillR = (byte)(BinaryPrimitives.ReadUInt16BigEndian(span.Slice(off + 10, 2)) >> 8);
            byte fillG = (byte)(BinaryPrimitives.ReadUInt16BigEndian(span.Slice(off + 12, 2)) >> 8);
            byte fillB = (byte)(BinaryPrimitives.ReadUInt16BigEndian(span.Slice(off + 14, 2)) >> 8);
            uint fillColor = 0xFF000000u | ((uint)fillR << 16) | ((uint)fillG << 8) | fillB;

            uint frameColor = fillColor; // default frame = fill
            if (recSize >= RecordSize)
            {
                byte frR = (byte)(BinaryPrimitives.ReadUInt16BigEndian(span.Slice(off + 16, 2)) >> 8);
                byte frG = (byte)(BinaryPrimitives.ReadUInt16BigEndian(span.Slice(off + 18, 2)) >> 8);
                byte frB = (byte)(BinaryPrimitives.ReadUInt16BigEndian(span.Slice(off + 20, 2)) >> 8);
                frameColor = 0xFF000000u | ((uint)frR << 16) | ((uint)frG << 8) | frB;
            }

            result.Add(new ColorRegion
            {
                PartId     = partId,
                Top        = top,
                Left       = left,
                Bottom     = bottom,
                Right      = right,
                FillColor  = fillColor,
                FrameColor = frameColor,
            });
        }

        return result;
    }
}

