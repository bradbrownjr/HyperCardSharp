using HyperCardSharp.Core.Binary;

namespace HyperCardSharp.Core.Bitmap;

/// <summary>
/// Decodes WOBA (Wrath of Bill Atkinson) compressed bitmap data from BMAP blocks.
/// WOBA uses row-by-row encoding with opcodes for RLE, XOR delta, bit-shift transforms,
/// and pattern fills. Mask and image layers are decoded separately then composited.
/// </summary>
public static class WobaDecoder
{
    /// <summary>
    /// Decode a BMAP block into a 1-bit bitmap at card dimensions.
    /// </summary>
    public static BitmapImage Decode(ReadOnlySpan<byte> blockData, BitmapBlock bmap)
    {
        int cardWidth = bmap.CardRect.Width;
        int cardHeight = bmap.CardRect.Height;
        if (cardWidth <= 0 || cardHeight <= 0)
            return new BitmapImage { Width = 0, Height = 0, RowBytes = 0 };

        int fullRowBytes = ((cardWidth + 31) / 32) * 4; // 32-bit aligned row width
        var image = new byte[fullRowBytes * cardHeight];
        var mask = new byte[fullRowBytes * cardHeight];

        // Decode mask layer
        if (bmap.MaskDataSize > 0 && !bmap.MaskRect.IsEmpty)
        {
            var maskData = blockData.Slice(bmap.MaskDataOffset, bmap.MaskDataSize);
            DecompressLayer(maskData, mask, bmap.MaskRect, bmap.CardRect, fullRowBytes);
        }
        else if (!bmap.MaskRect.IsEmpty)
        {
            // No compressed data but rect is non-zero: fill entire mask rect with 1s
            FillRect(mask, bmap.MaskRect, bmap.CardRect, fullRowBytes, 0xFF);
        }
        // else: mask is empty, handled in composition

        // Decode image layer
        if (bmap.ImageDataSize > 0 && !bmap.ImageRect.IsEmpty)
        {
            var imageData = blockData.Slice(bmap.ImageDataOffset, bmap.ImageDataSize);
            DecompressLayer(imageData, image, bmap.ImageRect, bmap.CardRect, fullRowBytes);
        }

        // Compose: if no mask data and mask rect is empty, use image as mask
        bool useSelfMask = bmap.MaskDataSize == 0 && bmap.MaskRect.IsEmpty;

        var result = new byte[fullRowBytes * cardHeight];
        for (int i = 0; i < result.Length; i++)
        {
            byte imgBit = image[i];
            byte mskBit = useSelfMask ? imgBit : mask[i];
            // Mask=1 → white, Image=1 → black (image overrides mask)
            // Result: image bits OR (mask bits that aren't image bits) as white background
            result[i] = imgBit; // For rendering: 1=black
            // For areas within the mask but not in image, we need to differentiate
            // from transparent areas. For now, just output the image layer.
            // Full composition will be handled at render time.
        }

        return new BitmapImage
        {
            Width = cardWidth,
            Height = cardHeight,
            RowBytes = fullRowBytes,
            Data = result
        };
    }

    /// <summary>
    /// Decompress a single WOBA layer (mask or image) into a card-sized buffer.
    /// </summary>
    private static void DecompressLayer(ReadOnlySpan<byte> data, byte[] output,
        MacRect layerRect, MacRect cardRect, int fullRowBytes)
    {
        int alignedLeft = layerRect.AlignedLeft;
        int alignedRight = layerRect.AlignedRight;
        int rowWidth = layerRect.AlignedRowBytes;

        if (rowWidth <= 0)
            return;

        int pos = 0; // position in compressed data stream
        int y = layerRect.Top;
        int x = 0; // byte position within current row

        int dx = 0; // horizontal XOR shift in bits
        int dy = 0; // vertical XOR offset in rows
        int repeat = 1;

        var rowBuffer = new byte[rowWidth];
        var patternBuffer = new byte[] { 0xAA, 0x55, 0xAA, 0x55, 0xAA, 0x55, 0xAA, 0x55 };

        // Store decompressed rows for dy lookback
        var rows = new byte[layerRect.Height][];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = new byte[rowWidth];

        while (pos < data.Length && y < layerRect.Bottom)
        {
            byte opcode = data[pos++];

            if (opcode >= 0xA0 && opcode <= 0xBF)
            {
                // Repeat prefix: next instruction repeats (op & 0x1F) times
                repeat = opcode & 0x1F;
                continue;
            }

            if (opcode >= 0x88 && opcode <= 0x8F)
            {
                // Transformation opcodes: set dx/dy
                switch (opcode)
                {
                    case 0x88: dx = 16; dy = 0; break;
                    case 0x89: dx = 0; dy = 0; break;
                    case 0x8A: dx = 0; dy = 1; break;
                    case 0x8B: dx = 0; dy = 2; break;
                    case 0x8C: dx = 1; dy = 0; break;
                    case 0x8D: dx = 1; dy = 1; break;
                    case 0x8E: dx = 2; dy = 2; break;
                    case 0x8F: dx = 8; dy = 0; break;
                }
                continue;
            }

            if (opcode >= 0x90 && opcode <= 0x9F)
            {
                // Unused range — end of data
                break;
            }

            if (opcode >= 0x80 && opcode <= 0x87)
            {
                // Whole-row opcodes
                for (int r = 0; r < repeat; r++)
                {
                    if (y >= layerRect.Bottom) break;

                    switch (opcode)
                    {
                        case 0x80:
                            // Uncompressed row data
                            if (pos + rowWidth <= data.Length)
                            {
                                data.Slice(pos, rowWidth).CopyTo(rowBuffer);
                            }
                            break;
                        case 0x81:
                            // All white (zeros)
                            Array.Clear(rowBuffer, 0, rowWidth);
                            break;
                        case 0x82:
                            // All black (0xFF)
                            Array.Fill(rowBuffer, (byte)0xFF);
                            break;
                        case 0x83:
                            // Repeated byte pattern
                            if (pos < data.Length)
                            {
                                byte patByte = data[pos]; // read on first repeat only, reuse
                                if (r == 0)
                                    patternBuffer[y % 8] = patByte;
                                Array.Fill(rowBuffer, patByte);
                            }
                            break;
                        case 0x84:
                            // Use pattern buffer
                            Array.Fill(rowBuffer, patternBuffer[y % 8]);
                            break;
                        case 0x85:
                            // Copy row y-1
                            CopyPreviousRow(rows, y - layerRect.Top, 1, rowBuffer, rowWidth);
                            break;
                        case 0x86:
                            // Copy row y-2
                            CopyPreviousRow(rows, y - layerRect.Top, 2, rowBuffer, rowWidth);
                            break;
                        case 0x87:
                            // Copy row y-3
                            CopyPreviousRow(rows, y - layerRect.Top, 3, rowBuffer, rowWidth);
                            break;
                    }

                    // Whole-row opcodes do NOT apply dx/dy transforms
                    int rowIdx = y - layerRect.Top;
                    if (rowIdx >= 0 && rowIdx < rows.Length)
                    {
                        Buffer.BlockCopy(rowBuffer, 0, rows[rowIdx], 0, rowWidth);
                        WriteRowToOutput(rows[rowIdx], output, y, cardRect, alignedLeft, fullRowBytes, rowWidth);
                    }
                    y++;
                }

                // Advance past inline data for 0x80 and 0x83
                if (opcode == 0x80)
                    pos += rowWidth;
                else if (opcode == 0x83)
                    pos++;

                repeat = 1;
                continue;
            }

            // Partial-row data opcodes: build row byte by byte
            for (int r = 0; r < repeat; r++)
            {
                int dataPos = pos; // Reset data position for each repeat iteration

                if (opcode <= 0x7F)
                {
                    // Low nibble = zero count, high nibble = data count
                    int zeroCount = opcode & 0x0F;
                    int dataCount = opcode >> 4;

                    // Write zeros
                    for (int z = 0; z < zeroCount && x < rowWidth; z++)
                        rowBuffer[x++] = 0;

                    // Write data bytes
                    for (int d = 0; d < dataCount && x < rowWidth; d++)
                    {
                        if (dataPos < data.Length)
                            rowBuffer[x++] = data[dataPos++];
                    }
                }
                else if (opcode >= 0xC0 && opcode <= 0xDF)
                {
                    // Literal data: (op & 0x1F) * 8 bytes
                    int count = (opcode & 0x1F) * 8;
                    for (int d = 0; d < count && x < rowWidth; d++)
                    {
                        if (dataPos < data.Length)
                            rowBuffer[x++] = data[dataPos++];
                    }
                }
                else if (opcode >= 0xE0 && opcode <= 0xFF)
                {
                    // Zero fill: (op & 0x1F) * 16 bytes
                    int count = (opcode & 0x1F) * 16;
                    for (int z = 0; z < count && x < rowWidth; z++)
                        rowBuffer[x++] = 0;
                }

                // Only advance data stream position on first repeat iteration
                if (r == 0)
                    pos = dataPos;
            }

            repeat = 1;

            // Check if row is complete
            if (x >= rowWidth)
            {
                // Apply dx/dy transformations
                int rowIdx = y - layerRect.Top;

                if (dx != 0)
                    ApplyDxTransform(rowBuffer, rowWidth, dx);

                if (dy != 0 && rowIdx >= dy)
                {
                    for (int i = 0; i < rowWidth; i++)
                        rowBuffer[i] ^= rows[rowIdx - dy][i];
                }

                // Store and output
                if (rowIdx >= 0 && rowIdx < rows.Length)
                {
                    Buffer.BlockCopy(rowBuffer, 0, rows[rowIdx], 0, rowWidth);
                    WriteRowToOutput(rows[rowIdx], output, y, cardRect, alignedLeft, fullRowBytes, rowWidth);
                }

                y++;
                x = 0;
                Array.Clear(rowBuffer, 0, rowWidth);
            }
        }
    }

    /// <summary>
    /// Apply horizontal XOR shift transform. Repeatedly shift right by dx bits and XOR.
    /// </summary>
    private static void ApplyDxTransform(byte[] row, int rowWidth, int dx)
    {
        int totalBits = rowWidth * 8;
        var shifted = new byte[rowWidth];

        // Copy original row
        Buffer.BlockCopy(row, 0, shifted, 0, rowWidth);

        int shiftAmount = dx;
        while (shiftAmount < totalBits)
        {
            ShiftRight(shifted, rowWidth, dx);
            for (int i = 0; i < rowWidth; i++)
                row[i] ^= shifted[i];
            shiftAmount += dx;
        }
    }

    /// <summary>
    /// Right-shift an entire byte array by 'sh' bits.
    /// Matches hypercard4net's shiftnstr exactly.
    /// </summary>
    private static void ShiftRight(byte[] s, int n, int sh)
    {
        int x = 0;
        for (int i = 0; i < n; i++)
        {
            x += (s[i] << 16) >> sh;
            s[i] = (byte)(x >> 16);
            x = (x & 0x0000FFFF) << 8;
        }
    }

    /// <summary>
    /// Copy a previous row for opcodes 0x85-0x87.
    /// </summary>
    private static void CopyPreviousRow(byte[][] rows, int currentRowIdx, int lookback,
        byte[] rowBuffer, int rowWidth)
    {
        int srcIdx = currentRowIdx - lookback;
        if (srcIdx >= 0 && srcIdx < rows.Length)
            Buffer.BlockCopy(rows[srcIdx], 0, rowBuffer, 0, rowWidth);
        else
            Array.Clear(rowBuffer, 0, rowWidth);
    }

    /// <summary>
    /// Write a decompressed row into the card-sized output buffer at the correct position.
    /// </summary>
    private static void WriteRowToOutput(byte[] row, byte[] output, int y,
        MacRect cardRect, int alignedLeft, int fullRowBytes, int rowWidth)
    {
        int cardY = y - cardRect.Top;
        if (cardY < 0 || cardY >= (cardRect.Bottom - cardRect.Top))
            return;

        int destByteOffset = alignedLeft / 8;
        int destStart = cardY * fullRowBytes + destByteOffset;
        int copyLen = Math.Min(rowWidth, fullRowBytes - destByteOffset);

        if (destStart >= 0 && destStart + copyLen <= output.Length && copyLen > 0)
            Buffer.BlockCopy(row, 0, output, destStart, copyLen);
    }

    /// <summary>
    /// Fill a rectangle region in the output with a byte value.
    /// </summary>
    private static void FillRect(byte[] output, MacRect rect, MacRect cardRect,
        int fullRowBytes, byte value)
    {
        int alignedLeft = rect.AlignedLeft;
        int alignedRight = rect.AlignedRight;
        int fillRowBytes = (alignedRight - alignedLeft) / 8;
        int destByteOffset = alignedLeft / 8;

        for (int y = rect.Top; y < rect.Bottom; y++)
        {
            int cardY = y - cardRect.Top;
            if (cardY < 0 || cardY >= (cardRect.Bottom - cardRect.Top))
                continue;

            int destStart = cardY * fullRowBytes + destByteOffset;
            int copyLen = Math.Min(fillRowBytes, fullRowBytes - destByteOffset);
            if (destStart >= 0 && destStart + copyLen <= output.Length && copyLen > 0)
                Array.Fill(output, value, destStart, copyLen);
        }
    }
}
