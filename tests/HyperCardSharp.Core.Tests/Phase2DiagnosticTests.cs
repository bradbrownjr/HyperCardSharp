using HyperCardSharp.Core.Binary;
using HyperCardSharp.Core.Stack;

namespace HyperCardSharp.Core.Tests;

public class Phase2DiagnosticTests
{
    private static byte[]? LoadSample()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            var path = Path.Combine(dir, "samples", "NEUROBLAST_HyperCard");
            if (File.Exists(path)) return File.ReadAllBytes(path);
            dir = Path.GetDirectoryName(dir)!;
        }
        return null;
    }

    [SkippableFact]
    public void DumpFirstCardBlock()
    {
        var data = LoadSample();
        Skip.If(data == null, "Sample not found");

        var parser = new StackParser();
        var stack = parser.Parse(data!);

        // Dump first CARD block header area
        var firstCard = stack.GetBlocks("CARD").First();
        var cardData = stack.GetBlockData(firstCard);

        Console.WriteLine($"First CARD: offset=0x{firstCard.FileOffset:X}, size={firstCard.Size}, id={firstCard.Id}");
        Console.WriteLine("Hex dump of first 128 bytes:");
        for (int i = 0; i < Math.Min(128, cardData.Length); i++)
        {
            if (i % 16 == 0) Console.Write($"  +0x{i:X3}: ");
            Console.Write($"{cardData[i]:X2} ");
            if (i % 16 == 15) Console.WriteLine();
        }
        Console.WriteLine();

        // Parse key fields based on format spec
        // After 16-byte header:
        // +0x10: bitmap ID (4 bytes) — ID of BMAP for this card, 0 = none
        // +0x14: flags (2 bytes)
        // +0x16: (reserved, 10 bytes)
        // +0x20: page ID (4 bytes) — page this card is on
        // +0x24: background ID (4 bytes) — which BKGD this card uses
        // +0x28: part count (2 bytes)
        // +0x2A: (next available part ID, 2 bytes)
        // +0x2C: total part+content data size (4 bytes)
        // +0x30: part content count (2 bytes)
        var bitmapId = BigEndianReader.ReadInt32At(cardData, 0x10);
        var bgId = BigEndianReader.ReadInt32At(cardData, 0x24);
        var partCount = BigEndianReader.ReadInt16At(cardData, 0x28);
        var partContentCount = BigEndianReader.ReadInt16At(cardData, 0x30);

        Console.WriteLine($"  bitmapId={bitmapId}, bgId={bgId}, parts={partCount}, contents={partContentCount}");
    }

    [SkippableFact]
    public void DumpListAndPageBlocks()
    {
        var data = LoadSample();
        Skip.If(data == null, "Sample not found");

        var parser = new StackParser();
        var stack = parser.Parse(data!);

        var listBlock = stack.GetBlocks("LIST").First();
        var listData = stack.GetBlockData(listBlock);
        Console.WriteLine($"LIST: offset=0x{listBlock.FileOffset:X}, size={listBlock.Size}");
        Console.WriteLine("First 64 bytes:");
        for (int i = 0; i < Math.Min(64, listData.Length); i++)
        {
            if (i % 16 == 0) Console.Write($"  +0x{i:X3}: ");
            Console.Write($"{listData[i]:X2} ");
            if (i % 16 == 15) Console.WriteLine();
        }
        Console.WriteLine();

        // LIST header after 16 bytes:
        // +0x10: page count (4 bytes)
        // +0x14: total page entry size (4 bytes)
        // +0x18+: page entries — each is 6 bytes: pageId (4) + cardCount (2)
        var pageCount = BigEndianReader.ReadInt32At(listData, 0x10);
        Console.WriteLine($"  pageCount={pageCount}");

        var pageBlock = stack.GetBlocks("PAGE").First();
        var pageData = stack.GetBlockData(pageBlock);
        Console.WriteLine($"\nPAGE: offset=0x{pageBlock.FileOffset:X}, size={pageBlock.Size}, id={pageBlock.Id}");
        // PAGE header after 16 bytes:
        // +0x10: list ID (4 bytes, back-reference)
        // +0x14: card count on this page (2 bytes)
        // +0x16+: card entries — each is cardId (4) + flags (2) = 6 bytes
        var cardCountOnPage = BigEndianReader.ReadInt16At(pageData, 0x14);
        Console.WriteLine($"  cardsOnPage={cardCountOnPage}");
        Console.WriteLine("  First 10 card IDs:");
        for (int i = 0; i < Math.Min(10, (int)cardCountOnPage); i++)
        {
            int offset = 0x16 + i * 6;
            var cardId = BigEndianReader.ReadInt32At(pageData, offset);
            var flags = BigEndianReader.ReadInt16At(pageData, offset + 4);
            Console.WriteLine($"    card[{i}]: id={cardId}, flags=0x{flags:X4}");
        }
    }

    [SkippableFact]
    public void DumpBackgroundBlock()
    {
        var data = LoadSample();
        Skip.If(data == null, "Sample not found");

        var parser = new StackParser();
        var stack = parser.Parse(data!);

        var bg = stack.GetBlocks("BKGD").First();
        var bgData = stack.GetBlockData(bg);
        Console.WriteLine($"BKGD: offset=0x{bg.FileOffset:X}, size={bg.Size}, id={bg.Id}");
        Console.WriteLine("First 128 bytes:");
        for (int i = 0; i < Math.Min(128, bgData.Length); i++)
        {
            if (i % 16 == 0) Console.Write($"  +0x{i:X3}: ");
            Console.Write($"{bgData[i]:X2} ");
            if (i % 16 == 15) Console.WriteLine();
        }
        Console.WriteLine();

        // BKGD layout same as CARD:
        var bitmapId = BigEndianReader.ReadInt32At(bgData, 0x10);
        var partCount = BigEndianReader.ReadInt16At(bgData, 0x28);
        var partContentCount = BigEndianReader.ReadInt16At(bgData, 0x30);
        Console.WriteLine($"  bitmapId={bitmapId}, parts={partCount}, contents={partContentCount}");
    }

    [SkippableFact]
    public void DumpFontTable()
    {
        var data = LoadSample();
        Skip.If(data == null, "Sample not found");

        var parser = new StackParser();
        var stack = parser.Parse(data!);

        var ftbl = stack.GetBlocks("FTBL").First();
        var ftblData = stack.GetBlockData(ftbl);
        Console.WriteLine($"FTBL: offset=0x{ftbl.FileOffset:X}, size={ftbl.Size}");
        Console.WriteLine("First 256 bytes:");
        for (int i = 0; i < Math.Min(256, ftblData.Length); i++)
        {
            if (i % 16 == 0) Console.Write($"  +0x{i:X3}: ");
            Console.Write($"{ftblData[i]:X2} ");
            if (i % 16 == 15) Console.WriteLine();
        }
        Console.WriteLine();

        // FTBL after 16-byte header:
        // +0x10: font count (4 bytes)
        // +0x14+: font entries — each is fontId (2 bytes) + Pascal string (1-byte length + chars)
        var fontCount = BigEndianReader.ReadInt32At(ftblData, 0x10);
        Console.WriteLine($"  fontCount={fontCount}");
    }
}
