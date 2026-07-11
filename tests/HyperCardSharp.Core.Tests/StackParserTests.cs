using HyperCardSharp.Core.Binary;
using HyperCardSharp.Core.Stack;

namespace HyperCardSharp.Core.Tests;

public class StackParserTests
{
    private static string? FindSampleFile()
    {
        // Walk up from test output directory to find the samples folder
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, "samples", "NEUROBLAST_HyperCard");
            if (File.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir)!;
        }
        return null;
    }

    [Fact]
    public void EnumerateBlocks_ReturnsMultipleBlocks()
    {
        // Synthetic minimal stack: STAK + TAIL
        var data = new byte[2048 + 32]; // STAK(2048) + TAIL(32)

        // STAK header
        data[0] = 0x00; data[1] = 0x00; data[2] = 0x08; data[3] = 0x00; // size = 2048
        data[4] = (byte)'S'; data[5] = (byte)'T'; data[6] = (byte)'A'; data[7] = (byte)'K';
        data[8] = 0xFF; data[9] = 0xFF; data[10] = 0xFF; data[11] = 0xFF; // id = -1

        // TAIL header at offset 2048
        data[2048] = 0x00; data[2049] = 0x00; data[2050] = 0x00; data[2051] = 0x20; // size = 32
        data[2052] = (byte)'T'; data[2053] = (byte)'A'; data[2054] = (byte)'I'; data[2055] = (byte)'L';
        data[2056] = 0xFF; data[2057] = 0xFF; data[2058] = 0xFF; data[2059] = 0xFF; // id = -1

        var parser = new StackParser();
        var blocks = parser.EnumerateBlocks(data).ToList();

        Assert.Equal(2, blocks.Count);
        Assert.Equal("STAK", blocks[0].Type);
        Assert.Equal("TAIL", blocks[1].Type);
    }

    [Fact]
    public void EnumerateBlocks_RejectsSizeSmallerThanHeader()
    {
        // A block claiming size=8 (less than the 16-byte header it must at least contain)
        var data = new byte[32];
        data[0] = 0x00; data[1] = 0x00; data[2] = 0x00; data[3] = 0x08; // size = 8 (invalid, < 16)
        data[4] = (byte)'S'; data[5] = (byte)'T'; data[6] = (byte)'A'; data[7] = (byte)'K';

        var parser = new StackParser();
        var blocks = parser.EnumerateBlocks(data).ToList();

        Assert.Empty(blocks); // enumeration stops immediately, no exception
    }

    [Fact]
    public void EnumerateBlocks_RejectsSizeExtendingPastEndOfFile()
    {
        // A block claiming size=2048 in a buffer that's only 64 bytes long —
        // this is the exact failure mode that used to reach an unguarded
        // Slice() in StackParser.Parse() and throw ArgumentOutOfRangeException
        // (reproduced by BeavisEmulatorV2.sit's mis-decompressed data).
        var data = new byte[64];
        data[0] = 0x00; data[1] = 0x00; data[2] = 0x08; data[3] = 0x00; // size = 2048
        data[4] = (byte)'S'; data[5] = (byte)'T'; data[6] = (byte)'A'; data[7] = (byte)'K';

        var parser = new StackParser();
        var blocks = parser.EnumerateBlocks(data).ToList();

        Assert.Empty(blocks); // enumeration stops before accepting the overrunning header
    }

    [Fact]
    public void Parse_ThrowsStackFormatException_NotBclException_OnCorruptData()
    {
        // Same overrun scenario as above, but exercised through Parse() end-to-end:
        // must surface a typed StackFormatException with offset context, never a
        // raw ArgumentOutOfRangeException.
        var data = new byte[64];
        data[0] = 0x00; data[1] = 0x00; data[2] = 0x08; data[3] = 0x00; // size = 2048
        data[4] = (byte)'S'; data[5] = (byte)'T'; data[6] = (byte)'A'; data[7] = (byte)'K';

        var parser = new StackParser();
        var ex = Assert.Throws<StackFormatException>(() => parser.Parse(data));
        Assert.Contains("No STAK block found", ex.Message);
    }

    [Fact]
    public void Parse_ThrowsStackFormatException_OnRandomGarbageData()
    {
        // Arbitrary non-stack data must fail gracefully with a typed exception,
        // not crash with an unhandled BCL exception (never-crash UX requirement).
        var random = new Random(42);
        var data = new byte[256];
        random.NextBytes(data);

        var parser = new StackParser();
        Assert.Throws<StackFormatException>(() => parser.Parse(data));
    }

    [SkippableFact]
    public void Parse_NeuroblastSample_ReadsStackHeader()
    {
        var path = FindSampleFile();
        Skip.If(path == null, "Sample file not found");

        var fileData = File.ReadAllBytes(path!);
        var parser = new StackParser();
        var stack = parser.Parse(fileData);

        // Verify STAK header
        Assert.Equal(10, stack.StackHeader.FormatVersion); // HC 2.x
        Assert.True(stack.StackHeader.CardCount > 0, "Card count should be positive");
        Assert.True(stack.StackHeader.BackgroundCount > 0, "Background count should be positive");

        // Verify we found multiple block types
        var blockTypes = stack.Blocks.Select(b => b.Type).Distinct().ToHashSet();
        Assert.Contains("STAK", blockTypes);
        Assert.Contains("MAST", blockTypes);
        Assert.Contains("CARD", blockTypes);
        Assert.Contains("BKGD", blockTypes);
        Assert.Contains("BMAP", blockTypes);
    }

    [SkippableFact]
    public void Parse_NeuroblastSample_PrintsBlockInventory()
    {
        var path = FindSampleFile();
        Skip.If(path == null, "Sample file not found");

        var fileData = File.ReadAllBytes(path!);
        var parser = new StackParser();
        var stack = parser.Parse(fileData);

        var inventory = StackParser.GetBlockInventory(stack.Blocks);

        // Should contain multiple block types
        Assert.Contains("STAK", inventory);
        Assert.Contains("CARD", inventory);
        Assert.Contains("BMAP", inventory);

        // Output for manual inspection
        Console.WriteLine($"Stack: version={stack.StackHeader.FormatVersion}, " +
                          $"cards={stack.StackHeader.CardCount}, " +
                          $"backgrounds={stack.StackHeader.BackgroundCount}, " +
                          $"size={stack.StackHeader.CardWidth}x{stack.StackHeader.CardHeight}");
        Console.WriteLine($"Blocks: {inventory}");
        Console.WriteLine($"Total blocks: {stack.Blocks.Count}");
    }
}
