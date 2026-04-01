using HyperCardSharp.Core.Binary;
using HyperCardSharp.Core.Bitmap;
using HyperCardSharp.Core.Resources;
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
    /// <param name="fileData">Raw stack (data fork) bytes.</param>
    /// <param name="resourceFork">
    /// Optional Mac resource fork for the stack file.  When provided, ICON resources
    /// are extracted and stored in <see cref="StackFile.Icons"/>.
    /// </param>
    public StackFile Parse(ReadOnlyMemory<byte> fileData, byte[]? resourceFork = null)
    {
        var data = fileData.Span;
        var blocks = EnumerateBlocks(fileData);
        StackBlock? stackBlock = null;
        MasterBlock? masterBlock = null;
        ListBlock? listBlock = null;
        FontTableBlock? fontTable = null;
        StyleTableBlock? styleTable = null;
        var cards = new List<CardBlock>();
        var backgrounds = new List<BackgroundBlock>();
        var pageHeaders = new List<BlockHeader>();
        var bitmaps = new Dictionary<int, BitmapBlock>();

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

                case "LIST":
                    listBlock = ListBlock.Parse(blockData, header);
                    _logger.LogInformation("LIST: {PageCount} pages, {CardCount} total cards, cardRefSize={RefSize}",
                        listBlock.PageCount, listBlock.TotalCardCount, listBlock.CardReferenceSize);
                    break;

                case "PAGE":
                    // Defer PAGE parsing until we have the LIST block's cardReferenceSize
                    pageHeaders.Add(header);
                    break;

                case "CARD":
                    var card = CardBlock.Parse(blockData, header);
                    cards.Add(card);
                    _logger.LogDebug("CARD {Id}: bg={BgId}, parts={Parts}, bitmap={Bitmap}",
                        header.Id, card.BackgroundId, card.Parts.Count, card.BitmapId);
                    break;

                case "BKGD":
                    var bg = BackgroundBlock.Parse(blockData, header);
                    backgrounds.Add(bg);
                    _logger.LogInformation("BKGD {Id}: {Cards} cards, {Parts} parts",
                        header.Id, bg.CardCount, bg.Parts.Count);
                    break;

                case "FTBL":
                    fontTable = FontTableBlock.Parse(blockData, header);
                    _logger.LogInformation("FTBL: {Count} fonts", fontTable.FontCount);
                    break;

                case "STBL":
                    styleTable = StyleTableBlock.Parse(blockData, header);
                    _logger.LogInformation("STBL: {Count} styles", styleTable.StyleCount);
                    break;

                case "BMAP":
                    var bmap = BitmapBlock.Parse(blockData, header);
                    bitmaps[header.Id] = bmap;
                    _logger.LogDebug("BMAP {Id}: card={CardRect}, mask={MaskRect}, image={ImageRect}",
                        header.Id, bmap.CardRect, bmap.MaskRect, bmap.ImageRect);
                    break;
            }
        }

        if (stackBlock == null)
            throw new InvalidDataException("No STAK block found in file");

        // Now parse PAGE blocks with the LIST's cardReferenceSize
        var pages = new List<PageBlock>();
        if (listBlock != null)
        {
            // Build a lookup from page block ID to expected card count
            var pageCardCounts = listBlock.PageReferences.ToDictionary(
                pr => pr.PageBlockId, pr => (int)pr.CardCount);

            foreach (var pageHeader in pageHeaders)
            {
                var pageData = data.Slice((int)pageHeader.FileOffset, pageHeader.Size);
                var expectedCards = pageCardCounts.GetValueOrDefault(pageHeader.Id, 0);
                var page = PageBlock.Parse(pageData, pageHeader, listBlock.CardReferenceSize, expectedCards);
                pages.Add(page);
                _logger.LogInformation("PAGE {Id}: {Count} card references", pageHeader.Id, page.CardReferences.Count);
            }
        }

        return new StackFile
        {
            StackHeader = stackBlock,
            MasterIndex = masterBlock,
            ListIndex = listBlock,
            FontTable = fontTable,
            StyleTable = styleTable,
            Cards = cards,
            Backgrounds = backgrounds,
            Pages = pages,
            Bitmaps = bitmaps,
            Blocks = blocks,
            RawData = fileData,
            Icons = ParseIconResources(resourceFork),
        };
    }

    private Dictionary<short, byte[]> ParseIconResources(byte[]? resourceFork)
    {
        var icons = MacResourceForkReader.GetResources(resourceFork, "ICON");
        if (icons.Count > 0)
            _logger.LogInformation("Resource fork: {Count} ICON resource(s) loaded.", icons.Count);
        return icons;
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
