using System.Buffers.Binary;
using HyperCardSharp.Core.Bitmap;
using HyperCardSharp.Core.Stack;

namespace HyperCardSharp.Core.Tests;

public class WobaDecoderTests
{
    private static StackFile? LoadStack()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            var path = Path.Combine(dir, "samples", "NEUROBLAST_HyperCard");
            if (File.Exists(path))
            {
                var data = File.ReadAllBytes(path);
                return new StackParser().Parse(data);
            }
            dir = Path.GetDirectoryName(dir)!;
        }
        return null;
    }

    [SkippableFact]
    public void BitmapBlock_ParsesAllBmaps()
    {
        var stack = LoadStack();
        Skip.If(stack == null, "Sample not found");

        // NEUROBLAST should have many BMAP blocks
        Assert.True(stack!.Bitmaps.Count > 0, "Should have BMAP blocks");
        Console.WriteLine($"Total BMAP blocks: {stack.Bitmaps.Count}");

        // Check that every card's bitmapId references a real BMAP (or is 0)
        foreach (var card in stack.Cards)
        {
            if (card.BitmapId != 0)
            {
                Assert.True(stack.Bitmaps.ContainsKey(card.BitmapId),
                    $"Card {card.Header.Id} references BMAP {card.BitmapId} which doesn't exist");
            }
        }
    }

    [SkippableFact]
    public void BitmapBlock_HeadersHaveValidRects()
    {
        var stack = LoadStack();
        Skip.If(stack == null, "Sample not found");

        foreach (var (id, bmap) in stack!.Bitmaps)
        {
            Assert.False(bmap.CardRect.IsEmpty, $"BMAP {id}: card rect is empty");
            Assert.True(bmap.CardRect.Width > 0 && bmap.CardRect.Width <= 1024,
                $"BMAP {id}: card rect width {bmap.CardRect.Width} out of range");
            Assert.True(bmap.CardRect.Height > 0 && bmap.CardRect.Height <= 1024,
                $"BMAP {id}: card rect height {bmap.CardRect.Height} out of range");
        }

        // Print first 5 BMAP headers
        Console.WriteLine("First 5 BMAP headers:");
        foreach (var (id, bmap) in stack.Bitmaps.Take(5))
        {
            Console.WriteLine($"  BMAP {id}: card=({bmap.CardRect.Left},{bmap.CardRect.Top},{bmap.CardRect.Right},{bmap.CardRect.Bottom})" +
                $" mask=({bmap.MaskRect.Left},{bmap.MaskRect.Top},{bmap.MaskRect.Right},{bmap.MaskRect.Bottom})" +
                $" image=({bmap.ImageRect.Left},{bmap.ImageRect.Top},{bmap.ImageRect.Right},{bmap.ImageRect.Bottom})" +
                $" maskSize={bmap.MaskDataSize} imgSize={bmap.ImageDataSize}");
        }
    }

    [SkippableFact]
    public void WobaDecoder_DecodesFirstBitmap()
    {
        var stack = LoadStack();
        Skip.If(stack == null, "Sample not found");

        var firstCard = stack!.Cards[0];
        Assert.NotEqual(0, firstCard.BitmapId);

        var bmap = stack.Bitmaps[firstCard.BitmapId];
        var blockData = stack.GetBlockData(stack.Blocks.First(b => b.Id == firstCard.BitmapId && b.Type == "BMAP"));
        var bitmap = WobaDecoder.Decode(blockData, bmap);

        Assert.Equal(bmap.CardRect.Width, bitmap.Width);
        Assert.Equal(bmap.CardRect.Height, bitmap.Height);
        Assert.True(bitmap.Data.Length > 0);

        // Bitmap should have some black pixels (not all white)
        bool hasBlack = bitmap.Data.Any(b => b != 0);
        Assert.True(hasBlack, "Decoded bitmap is all white — likely a decoding error");

        // Should also have some white pixels (not all black)
        bool hasWhite = bitmap.Data.Any(b => b != 0xFF);
        Assert.True(hasWhite, "Decoded bitmap is all black — likely a decoding error");

        Console.WriteLine($"Decoded bitmap: {bitmap.Width}x{bitmap.Height}, " +
            $"rowBytes={bitmap.RowBytes}, data={bitmap.Data.Length} bytes");

        // Count black pixel percentage
        int blackBits = 0;
        for (int i = 0; i < bitmap.Data.Length; i++)
        {
            byte b = bitmap.Data[i];
            while (b != 0)
            {
                blackBits += b & 1;
                b >>= 1;
            }
        }
        int totalBits = bitmap.Width * bitmap.Height;
        double blackPct = 100.0 * blackBits / totalBits;
        Console.WriteLine($"Black pixel percentage: {blackPct:F1}%");
    }

    [SkippableFact]
    public void WobaDecoder_DecodesAllBitmaps_NoExceptions()
    {
        var stack = LoadStack();
        Skip.If(stack == null, "Sample not found");

        int decoded = 0;
        int errors = 0;
        var bmapBlocks = stack!.Blocks.Where(b => b.Type == "BMAP").ToList();

        foreach (var blockHeader in bmapBlocks)
        {
            if (!stack.Bitmaps.TryGetValue(blockHeader.Id, out var bmap))
                continue;

            try
            {
                var blockData = stack.GetBlockData(blockHeader);
                var bitmap = WobaDecoder.Decode(blockData, bmap);
                Assert.True(bitmap.Width > 0 && bitmap.Height > 0,
                    $"BMAP {blockHeader.Id}: decoded to {bitmap.Width}x{bitmap.Height}");
                decoded++;
            }
            catch (Exception ex)
            {
                errors++;
                Console.WriteLine($"BMAP {blockHeader.Id} FAILED: {ex.Message}");
            }
        }

        Console.WriteLine($"Decoded {decoded}/{bmapBlocks.Count} BMAPs, {errors} errors");
        Assert.Equal(0, errors);
        Assert.Equal(bmapBlocks.Count, decoded);
    }

    /// <summary>
    /// Builds a minimal 16x16 raw BMAP block (header fields + WOBA-compressed
    /// mask/image data) using whole-row opcodes so each row is exactly one byte:
    /// 0x81 = all white (bit 0), 0x82 = all black (bit 1). Row semantics:
    ///   row 0: image=black -> always opaque black regardless of mask
    ///   row 1: image=white, mask=black (opaque) -> opaque white
    ///   row 2: image=white, mask=white (0)      -> transparent
    /// Rows 3-15 are filler (white/transparent) and not asserted on.
    /// </summary>
    private static (byte[] BlockData, BitmapBlock Bmap) BuildThreeStateFixture()
    {
        const int size = 16;
        byte[] maskOpcodes  = { 0x82, 0x82, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81 };
        byte[] imageOpcodes = { 0x82, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81 };

        const int maskDataOffset = 0x40;
        int imageDataOffset = maskDataOffset + maskOpcodes.Length;
        int totalSize = imageDataOffset + imageOpcodes.Length;

        var data = new byte[totalSize];
        void WriteRect(int offset, short top, short left, short bottom, short right)
        {
            BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(offset, 2), top);
            BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(offset + 2, 2), left);
            BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(offset + 4, 2), bottom);
            BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(offset + 6, 2), right);
        }

        WriteRect(0x18, 0, 0, size, size); // cardRect
        WriteRect(0x20, 0, 0, size, size); // maskRect
        WriteRect(0x28, 0, 0, size, size); // imageRect
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0x38, 4), maskOpcodes.Length);  // maskDataSize
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0x3C, 4), imageOpcodes.Length); // imageDataSize
        maskOpcodes.CopyTo(data.AsSpan(maskDataOffset));
        imageOpcodes.CopyTo(data.AsSpan(imageDataOffset));

        var header = new HyperCardSharp.Core.Binary.BlockHeader { Size = totalSize, Type = "BMAP", Id = 1, FileOffset = 0 };
        var bmap = BitmapBlock.Parse(data, header);
        return (data, bmap);
    }

    [Fact]
    public void WobaDecoder_WithRealMaskData_PopulatesDistinctMask()
    {
        var (blockData, bmap) = BuildThreeStateFixture();
        var bitmap = WobaDecoder.Decode(blockData, bmap);

        // A genuine separate mask layer must not equal Data (that would mean
        // the decoder fell back to self-masking instead of using the real mask).
        Assert.NotSame(bitmap.Data, bitmap.Mask);

        // Row 0: image bit = 1 (black ink)
        Assert.True(GetBit(bitmap.Data, bitmap.RowBytes, 0, 0));
        // Row 1: image bit = 0, mask bit = 1 (opaque white)
        Assert.False(GetBit(bitmap.Data, bitmap.RowBytes, 0, 1));
        Assert.True(GetBit(bitmap.Mask, bitmap.RowBytes, 0, 1));
        // Row 2: image bit = 0, mask bit = 0 (transparent)
        Assert.False(GetBit(bitmap.Data, bitmap.RowBytes, 0, 2));
        Assert.False(GetBit(bitmap.Mask, bitmap.RowBytes, 0, 2));
    }

    [Fact]
    public void WobaDecoder_SelfMasking_MaskEqualsData()
    {
        var (blockData, bmap) = BuildThreeStateFixture();

        // Force self-masking: zero out maskDataSize and collapse maskRect to empty.
        BinaryPrimitives.WriteInt32BigEndian(blockData.AsSpan(0x38, 4), 0); // maskDataSize = 0
        BinaryPrimitives.WriteInt16BigEndian(blockData.AsSpan(0x20, 2), 0); // maskRect.Top = 0
        BinaryPrimitives.WriteInt16BigEndian(blockData.AsSpan(0x24, 2), 0); // maskRect.Bottom = 0 -> empty rect

        var header = new HyperCardSharp.Core.Binary.BlockHeader { Size = blockData.Length, Type = "BMAP", Id = 1, FileOffset = 0 };
        var selfMaskBmap = BitmapBlock.Parse(blockData, header);
        var bitmap = WobaDecoder.Decode(blockData, selfMaskBmap);

        // Self-masking: only inked (image bit=1) pixels are opaque, so Mask == Data.
        Assert.Same(bitmap.Data, bitmap.Mask);
    }

    private static bool GetBit(byte[] data, int rowBytes, int x, int y)
    {
        int byteIndex = y * rowBytes + x / 8;
        int bitIndex = 7 - (x % 8);
        return (data[byteIndex] & (1 << bitIndex)) != 0;
    }
}
