using HyperCardSharp.Core.Binary;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HyperCardSharp.Core.Stack;

/// <summary>
/// Parses a HyperCard stack file by walking its block structure.
/// </summary>
public class StackParser
{
    private readonly ILogger _logger;

    public StackParser(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Enumerate all block headers in the stack file sequentially.
    /// </summary>
    public List<BlockHeader> EnumerateBlocks(ReadOnlyMemory<byte> fileData)
    {
        var data = fileData.Span;
        var results = new List<BlockHeader>();
        int offset = 0;

        while (offset + 16 <= data.Length)
        {
            var header = BlockHeader.Parse(data.Slice(offset), offset);

            if (header.Size <= 0)
            {
                _logger.LogWarning("Invalid block size {Size} at offset 0x{Offset:X}. Stopping enumeration.",
                    header.Size, offset);
                break;
            }

            if (!IsKnownBlockType(header.Type))
            {
                _logger.LogWarning("Unknown block type '{Type}' at offset 0x{Offset:X}, size={Size}",
                    header.Type, offset, header.Size);
            }

            results.Add(header);

            // TAIL is the last block
            if (header.Type == "TAIL")
                break;

            offset += header.Size;
        }

        return results;
    }

    /// <summary>
    /// Parse a complete stack file into a StackFile model.
    /// </summary>
    public StackFile Parse(ReadOnlyMemory<byte> fileData)
    {
        var data = fileData.Span;
        var blocks = EnumerateBlocks(fileData);
        StackBlock? stackBlock = null;
        MasterBlock? masterBlock = null;

        foreach (var header in blocks)
        {
            var blockData = data.Slice((int)header.FileOffset, header.Size);

            switch (header.Type)
            {
                case "STAK":
                    stackBlock = StackBlock.Parse(blockData, header);
                    _logger.LogInformation(
                        "STAK: version={Version}, cards={Cards}, backgrounds={Backgrounds}, size={Width}x{Height}",
                        stackBlock.FormatVersion, stackBlock.CardCount, stackBlock.BackgroundCount,
                        stackBlock.CardWidth, stackBlock.CardHeight);
                    break;

                case "MAST":
                    masterBlock = MasterBlock.Parse(blockData, header);
                    var nonZeroOffsets = masterBlock.Offsets.Count(o => o != 0);
                    _logger.LogInformation("MAST: {Count} non-zero offset entries", nonZeroOffsets);
                    break;
            }
        }

        if (stackBlock == null)
            throw new InvalidDataException("No STAK block found in file");

        return new StackFile
        {
            StackHeader = stackBlock,
            MasterIndex = masterBlock,
            Blocks = blocks,
            RawData = fileData
        };
    }

    /// <summary>
    /// Generate a human-readable block inventory summary.
    /// </summary>
    public static string GetBlockInventory(IReadOnlyList<BlockHeader> blocks)
    {
        var counts = blocks
            .GroupBy(b => b.Type)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Count()}x {g.Key}");

        return string.Join(", ", counts);
    }

    private static bool IsKnownBlockType(string type) => type switch
    {
        "STAK" or "MAST" or "LIST" or "PAGE" or "CARD" or "BKGD" or
        "BMAP" or "STBL" or "FTBL" or "FREE" or "TAIL" or
        "PRNT" or "PRST" or "PRFT" => true,
        _ => false
    };
}
