using System.Buffers.Binary;
using SkiaSharp;

namespace HyperCardSharp.Rendering;

/// <summary>
/// Decodes classic Mac PICT (Picture) resources into SkiaSharp bitmaps.
///
/// Supported opcode set (PICT v2):
///   0x0001  ClipRegion      — skip clip rect data
///   0x0011  VersionOp       — version byte (must be 2 for PICT v2)
///   0x001E  DefHilite       — no data
///   0x001F  RGBFgColor      — skip 6-byte RGBColor
///   0x001A  RGBFgColor alt  — skip 6 bytes
///   0x001B  RGBBkColor      — skip 6 bytes
///   0x0C00  HeaderOp        — skip 24-byte header
///   0x0098  PackBitsRect    — RLE-compressed 1-bit or 8-bit indexed bitmap
///   0x009A  DirectBitsRect  — RLE-compressed 32-bit or 16-bit direct-color bitmap
///   0x00FF  EndPic          — end of picture
///
/// Any unrecognised opcode is logged and the data section is skipped using
/// the PICT v2 "reserved" skip rules to avoid crashing.
///
/// Reference: Inside Macintosh: Imaging With QuickDraw, Chapter 7.
/// TODO: verify against real PICT resources from HyperCard stacks.
/// </summary>
public static class PictDecoder
{
    /// <summary>
    /// Decode a PICT resource byte array into an SKBitmap.
    /// Returns null if the format is unrecognized, empty, or fatally malformed.
    /// </summary>
    public static SKBitmap? Decode(byte[] pictData)
    {
        if (pictData == null || pictData.Length < 12)
            return null;

        try
        {
            return DecodeCore(pictData);
        }
        catch
        {
            return null;
        }
    }

    private static SKBitmap? DecodeCore(byte[] data)
    {
        // PICT v1: 2-byte picture size + 8-byte bounding box, then 1-byte opcodes
        // PICT v2: 512-byte leading zeros (in resource fork), then same as v1 start,
        //          but actual content starts with VersionOp (0x0011) opcode.
        //
        // For a resource fork PICT, data starts at offset 0 (512-byte preamble stripped by resource reader).
        // For an in-stream PICT, there may be a 512-byte header of zeros.

        var span = data.AsSpan();
        int pos = 0;

        // Skip the 2-byte picture size (often unreliable in v2)
        // and read the 8-byte bounding rect.
        if (pos + 10 > data.Length) return null;
        pos += 2; // picture size

        short pictTop    = BinaryPrimitives.ReadInt16BigEndian(span.Slice(pos + 0, 2));
        short pictLeft   = BinaryPrimitives.ReadInt16BigEndian(span.Slice(pos + 2, 2));
        short pictBottom = BinaryPrimitives.ReadInt16BigEndian(span.Slice(pos + 4, 2));
        short pictRight  = BinaryPrimitives.ReadInt16BigEndian(span.Slice(pos + 6, 2));
        pos += 8;

        int picWidth  = pictRight  - pictLeft;
        int picHeight = pictBottom - pictTop;
        if (picWidth <= 0 || picWidth > 4096 || picHeight <= 0 || picHeight > 4096)
            return null;

        // Walk opcodes
        SKBitmap? result = null;

        while (pos + 2 <= data.Length)
        {
            int opPos = pos;
            ushort op = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(pos, 2));
            pos += 2;

            switch (op)
            {
                case 0x0000: // NOP
                    break;
                case 0x0001: // ClipRegion: 2-byte rgn size + data
                {
                    if (pos + 2 > data.Length) return result;
                    ushort rgnSize = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(pos, 2));
                    pos += rgnSize;
                    break;
                }
                case 0x0011: // VersionOp: 2-byte version (version=02FF for PICT v2)
                    pos += 2;
                    break;
                case 0x001E: // DefHilite: no data
                case 0x001F: // same
                    break;
                case 0x001A: // RGBFgColor
                case 0x001B: // RGBBkColor
                    pos += 6;
                    break;
                case 0x001C: // HiliteMode: no data
                    break;
                case 0x001D: // HiliteColor: 6 bytes
                    pos += 6;
                    break;
                case 0x0002: // BkPat: 8 bytes
                case 0x0003: // TxFont: 2 bytes
                case 0x0004: // TxFace: 1 byte
                    pos += 2; break; // TxFace is actually 1 but padded to 2
                case 0x0005: // TxMode: 2 bytes
                case 0x0006: // SpExtra: 4 bytes (Fixed)
                    pos += (op == 0x0006) ? 4 : 2; break;
                case 0x0007: // PnSize: 4 bytes
                    pos += 4; break;
                case 0x0008: // PnMode: 2 bytes
                    pos += 2; break;
                case 0x0009: // PnPat: 8 bytes
                case 0x000A: // FillPat: 8 bytes
                    pos += 8; break;
                case 0x000B: // OvSize: 4 bytes
                    pos += 4; break;
                case 0x000D: // TxSize: 2 bytes
                    pos += 2; break;
                case 0x000F: // FractEnable: 2 bytes
                    pos += 2; break;
                case 0x0010: // TxRatio: 8 bytes
                    pos += 8; break;
                case 0x0020: // Line: 8 bytes
                case 0x0021: // LineFrom: 4 bytes
                    pos += (op == 0x0020) ? 8 : 4; break;
                case 0x0022: // ShortLine: 6 bytes
                    pos += 6; break;
                case 0x0023: // ShortLineFrom: 2 bytes
                    pos += 2; break;
                case 0x0030: case 0x0031: case 0x0032: case 0x0033:
                case 0x0034: case 0x0035: case 0x0036: case 0x0037:
                    // FrameRect/PaintRect etc: 8-byte rect
                    pos += 8; break;
                case 0x0040: case 0x0041: case 0x0042: case 0x0043:
                case 0x0044: case 0x0045: case 0x0046: case 0x0047:
                    // Oval opcodes: 8-byte rect
                    pos += 8; break;
                case 0x0C00: // HeaderOp: 24 bytes
                    pos += 24; break;
                case 0x8200: // CompressedQuickTime: variable
                {
                    if (pos + 4 > data.Length) return result;
                    int qtLen = (int)BinaryPrimitives.ReadUInt32BigEndian(span.Slice(pos, 4));
                    pos += 4 + qtLen;
                    break;
                }
                case 0x0098: // PackBitsRect
                    result = ReadPackBitsRect(data, ref pos, picWidth, picHeight) ?? result;
                    break;
                case 0x009A: // DirectBitsRect
                    result = ReadDirectBitsRect(data, ref pos, picWidth, picHeight) ?? result;
                    break;
                case 0x00FF: // EndPic
                    return result;
                default:
                    // PICT v2 skip rules for unknown opcodes:
                    // 0x00A0–0x00AF: 2-byte data  
                    // 0x00B0–0x00CF: no data
                    // 0x00D0–0x00FE: 4-byte data
                    // 0x0100–0x7FFF: 2-byte skip count, then that many bytes
                    // 0x8000–0x80FF: 4-byte data
                    // 0x8100–0xFFFF: 4-byte size, then that many bytes
                    if (op >= 0x00B0 && op <= 0x00CF)
                        break;
                    if (op >= 0x00A0 && op <= 0x00AF)
                    { pos += 2; break; }
                    if (op >= 0x00D0 && op <= 0x00FE)
                    { pos += 4; break; }
                    if (op >= 0x0100 && op <= 0x7FFF)
                    {
                        if (pos + 2 > data.Length) return result;
                        ushort skip = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(pos, 2));
                        pos += 2 + skip * 2;
                        break;
                    }
                    if (op >= 0x8000 && op <= 0x80FF)
                    { pos += 4; break; }
                    if (op >= 0x8100)
                    {
                        if (pos + 4 > data.Length) return result;
                        int skip = (int)BinaryPrimitives.ReadUInt32BigEndian(span.Slice(pos, 4));
                        pos += 4 + skip;
                        break;
                    }
                    break;
            }
        }

        return result;
    }

    // ── PackBitsRect (opcode 0x0098) ─────────────────────────────────────────

    private static SKBitmap? ReadPackBitsRect(byte[] data, ref int pos, int picW, int picH)
    {
        var span = data.AsSpan();
        if (pos + 14 > data.Length) return null;

        // BitMap or PixMap record starts with rowBytes (2 bytes, high bit = PixMap)
        ushort rowBytesRaw = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(pos, 2));
        bool isPixMap = (rowBytesRaw & 0x8000) != 0;
        int rowBytes = rowBytesRaw & 0x7FFF;
        pos += 2;

        // Bounds rect
        short bTop    = BinaryPrimitives.ReadInt16BigEndian(span.Slice(pos,     2));
        short bLeft   = BinaryPrimitives.ReadInt16BigEndian(span.Slice(pos + 2, 2));
        short bBottom = BinaryPrimitives.ReadInt16BigEndian(span.Slice(pos + 4, 2));
        short bRight  = BinaryPrimitives.ReadInt16BigEndian(span.Slice(pos + 6, 2));
        pos += 8;
        int bmpWidth  = bRight  - bLeft;
        int bmpHeight = bBottom - bTop;
        if (bmpWidth <= 0 || bmpHeight <= 0) return null;

        int pixelSize = 1; // default for BitMap
        SKColor[]? colorTable = null;

        if (isPixMap)
        {
            // PixMap record: 36 additional bytes after bounds
            if (pos + 36 > data.Length) return null;
            // pmVersion(2), packType(2), packSize(4), hRes(4), vRes(4), pixelType(2),
            // pixelSize(2), cmpCount(2), cmpSize(2), planeBytes(4), pmTable(4), pmReserved(4)
            pos += 2; // pmVersion
            pos += 2; // packType
            pos += 4; // packSize
            pos += 4; // hRes
            pos += 4; // vRes
            pos += 2; // pixelType
            pixelSize = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(pos, 2));
            pos += 2; // pixelSize
            pos += 2; // cmpCount
            pos += 2; // cmpSize
            pos += 4; // planeBytes
            pos += 4; // pmTable (handle)
            pos += 4; // pmReserved

            // ColorTable
            if (pos + 8 > data.Length) return null;
            // ctSeed(4), transIndex(2), ctSize (count-1)(2)
            pos += 4; // ctSeed
            pos += 2; // transIndex
            short ctSizeMinus1 = BinaryPrimitives.ReadInt16BigEndian(span.Slice(pos, 2));
            pos += 2;
            int ctSize = ctSizeMinus1 + 1;
            // Each color table entry: value(2) + RGBColor(6 bytes = R16, G16, B16) = 8 bytes
            colorTable = new SKColor[ctSize];
            for (int ci = 0; ci < ctSize; ci++)
            {
                if (pos + 8 > data.Length) break;
                // value field (index for indexed PixMaps, ignored for direct)
                ushort ctIndex = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(pos, 2));
                pos += 2;
                byte r = span[pos];     // high byte of 16-bit component
                pos += 2;
                byte g = span[pos];
                pos += 2;
                byte b = span[pos];
                pos += 2;
                int idx = (pixelSize <= 8 && ctIndex < ctSize) ? ctIndex : ci;
                if (idx < colorTable.Length)
                    colorTable[idx] = new SKColor(r, g, b);
            }
        }

        // srcRect, dstRect, mode = 8+8+2 = 18 bytes
        if (pos + 18 > data.Length) return null;
        pos += 18;

        // PackBits data: for rowBytes <= 250, each row stored uncompressed;
        // for rowBytes > 250, each row preceded by 2-byte row byte count;
        // for rowBytes <= 250, each row preceded by 1-byte row byte count.
        bool useLongRowCount = rowBytes > 250;

        var bitmap = new SKBitmap(bmpWidth, bmpHeight, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        for (int row = 0; row < bmpHeight; row++)
        {
            int rowByteCount;
            if (useLongRowCount)
            {
                if (pos + 2 > data.Length) break;
                rowByteCount = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(pos, 2));
                pos += 2;
            }
            else
            {
                if (pos + 1 > data.Length) break;
                rowByteCount = span[pos++];
            }

            if (pos + rowByteCount > data.Length) break;

            var rowData = UnpackBits(span.Slice(pos, rowByteCount), rowBytes);
            pos += rowByteCount;

            // Draw the row
            if (pixelSize == 1 && rowData.Length >= rowBytes)
            {
                for (int x = 0; x < bmpWidth; x++)
                {
                    int bi = x / 8;
                    int bit = 7 - (x % 8);
                    bool isBlack = bi < rowData.Length && ((rowData[bi] >> bit) & 1) != 0;
                    bitmap.SetPixel(x, row, isBlack ? SKColors.Black : SKColors.White);
                }
            }
            else if (pixelSize == 2 && colorTable != null)
            {
                for (int x = 0; x < bmpWidth; x++)
                {
                    int bi = x / 4;
                    int shift = 6 - (x % 4) * 2;
                    int idx = bi < rowData.Length ? (rowData[bi] >> shift) & 0x03 : 0;
                    bitmap.SetPixel(x, row, idx < colorTable.Length ? colorTable[idx] : SKColors.White);
                }
            }
            else if (pixelSize == 4 && colorTable != null)
            {
                for (int x = 0; x < bmpWidth; x++)
                {
                    int bi = x / 2;
                    int shift = (x % 2 == 0) ? 4 : 0;
                    int idx = bi < rowData.Length ? (rowData[bi] >> shift) & 0x0F : 0;
                    bitmap.SetPixel(x, row, idx < colorTable.Length ? colorTable[idx] : SKColors.White);
                }
            }
            else if (pixelSize == 8 && colorTable != null)
            {
                for (int x = 0; x < bmpWidth && x < rowData.Length; x++)
                {
                    int idx = rowData[x];
                    bitmap.SetPixel(x, row, idx < colorTable.Length ? colorTable[idx] : SKColors.White);
                }
            }
            // Other pixel depths: leave as white
        }

        return bitmap;
    }

    // ── DirectBitsRect (opcode 0x009A) ──────────────────────────────────────

    private static SKBitmap? ReadDirectBitsRect(byte[] data, ref int pos, int picW, int picH)
    {
        var span = data.AsSpan();

        // baseAddr: 4 bytes (always 0x000000FF in file)
        if (pos + 4 > data.Length) return null;
        pos += 4;

        // PixMap record
        if (pos + 2 > data.Length) return null;
        ushort rowBytesRaw = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(pos, 2));
        int rowBytes = rowBytesRaw & 0x7FFF;
        pos += 2;

        // Bounds rect
        if (pos + 8 > data.Length) return null;
        short bTop    = BinaryPrimitives.ReadInt16BigEndian(span.Slice(pos,     2));
        short bLeft   = BinaryPrimitives.ReadInt16BigEndian(span.Slice(pos + 2, 2));
        short bBottom = BinaryPrimitives.ReadInt16BigEndian(span.Slice(pos + 4, 2));
        short bRight  = BinaryPrimitives.ReadInt16BigEndian(span.Slice(pos + 6, 2));
        pos += 8;
        int bmpWidth  = bRight  - bLeft;
        int bmpHeight = bBottom - bTop;
        if (bmpWidth <= 0 || bmpHeight <= 0) return null;

        // Remaining PixMap fields: pmVersion(2), packType(2), packSize(4), hRes(4), vRes(4),
        // pixelType(2), pixelSize(2), cmpCount(2), cmpSize(2), planeBytes(4), pmTable(4),
        // pmExtensions(4) = 40 bytes
        if (pos + 40 > data.Length) return null;
        pos += 4;  // pmVersion + packType
        pos += 4;  // packSize
        pos += 8;  // hRes, vRes
        pos += 2;  // pixelType
        ushort pixelSize = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(pos, 2));
        pos += 2;
        pos += 2;  // cmpCount
        pos += 2;  // cmpSize
        pos += 4;  // planeBytes
        pos += 4;  // pmTable
        pos += 4;  // pmExtensions

        // srcRect, dstRect, mode = 18 bytes
        if (pos + 18 > data.Length) return null;
        pos += 18;

        // Row data (packType 4 = 32-bit → PackBits compressed per component row;
        //            packType 3 = 16-bit → PackBits compressed rows)
        bool useLongRowCount = rowBytes > 250;

        var bitmap = new SKBitmap(bmpWidth, bmpHeight, SKColorType.Bgra8888, SKAlphaType.Opaque);

        for (int row = 0; row < bmpHeight; row++)
        {
            int rowByteCount;
            if (useLongRowCount)
            {
                if (pos + 2 > data.Length) break;
                rowByteCount = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(pos, 2));
                pos += 2;
            }
            else
            {
                if (pos + 1 > data.Length) break;
                rowByteCount = span[pos++];
            }

            if (pos + rowByteCount > data.Length) break;

            var rowData = UnpackBits(span.Slice(pos, rowByteCount), rowBytes);
            pos += rowByteCount;

            if (pixelSize == 32 && rowData.Length >= bmpWidth * 4)
            {
                // PackType 4: channels stored as ARGB per row
                // rowData = [A channel * width][R channel * width][G channel * width][B channel * width]
                for (int x = 0; x < bmpWidth; x++)
                {
                    byte a = rowData[x];
                    byte r = rowData[bmpWidth + x];
                    byte g = rowData[bmpWidth * 2 + x];
                    byte b = rowData[bmpWidth * 3 + x];
                    bitmap.SetPixel(x, row, new SKColor(r, g, b, a == 0 ? (byte)255 : a));
                }
            }
            else if (pixelSize == 16 && rowData.Length >= bmpWidth * 2)
            {
                // 16-bit: 5-5-5 RGB packed into uint16
                for (int x = 0; x < bmpWidth; x++)
                {
                    ushort pix = (ushort)((rowData[x * 2] << 8) | rowData[x * 2 + 1]);
                    byte r = (byte)((pix >> 10 & 0x1F) << 3);
                    byte g = (byte)((pix >>  5 & 0x1F) << 3);
                    byte b = (byte)((pix       & 0x1F) << 3);
                    bitmap.SetPixel(x, row, new SKColor(r, g, b));
                }
            }
        }

        return bitmap;
    }

    // ── PackBits decompressor ────────────────────────────────────────────────

    /// <summary>
    /// Decompress Mac PackBits encoded data into a byte array of length <paramref name="expectedLen"/>.
    /// If decompression yields fewer bytes, the remainder is zero-padded.
    /// </summary>
    private static byte[] UnpackBits(ReadOnlySpan<byte> src, int expectedLen)
    {
        var dst = new byte[Math.Max(expectedLen, 1)];
        int si = 0, di = 0;

        while (si < src.Length && di < dst.Length)
        {
            sbyte n = (sbyte)src[si++];
            if (n >= 0)
            {
                // Literal run: copy (n+1) bytes
                int count = n + 1;
                for (int k = 0; k < count && si < src.Length && di < dst.Length; k++)
                    dst[di++] = src[si++];
            }
            else if (n != -128)
            {
                // Replicate run: repeat next byte (-n+1) times
                int count = -n + 1;
                if (si >= src.Length) break;
                byte val = src[si++];
                for (int k = 0; k < count && di < dst.Length; k++)
                    dst[di++] = val;
            }
            // n == -128 is a NOP
        }

        return dst;
    }
}
