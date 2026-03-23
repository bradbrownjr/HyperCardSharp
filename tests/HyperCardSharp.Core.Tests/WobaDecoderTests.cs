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
}
