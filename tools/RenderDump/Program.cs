using HyperCardSharp.Core.Containers;
using HyperCardSharp.Core.Stack;
using HyperCardSharp.Rendering;
using SkiaSharp;

if (args.Length < 1)
{
    PrintUsage();
    return 1;
}

bool infoMode = args[0] == "--info";
var positional = infoMode ? args[1..] : args;

if (positional.Length < 1 || (!infoMode && positional.Length < 2))
{
    PrintUsage();
    return 1;
}

var inputPath = positional[0];
var outDir = infoMode ? null : positional[1];
int maxCards = positional.Length > (infoMode ? 1 : 2)
    ? int.Parse(positional[infoMode ? 1 : 2])
    : int.MaxValue;

if (!File.Exists(inputPath))
{
    Console.Error.WriteLine($"File not found: {inputPath}");
    return 1;
}

var fileBytes = File.ReadAllBytes(inputPath);
Console.WriteLine($"Input: {inputPath} ({fileBytes.Length:N0} bytes)");

var entries = ContainerPipeline.UnwrapEntries(fileBytes, msg => Console.WriteLine($"  log: {msg}"));
Console.WriteLine($"Stacks found: {entries.Count}");
foreach (var e in entries)
    Console.WriteLine($"  '{e.Name}' {e.SizeText} {e.Resolution} cards={e.CardCount} resourceFork={(e.ResourceFork?.Length.ToString() ?? "none")}");

if (entries.Count == 0)
{
    Console.Error.WriteLine("No stack could be extracted from this file.");
    return 1;
}

var entry = entries[0];
StackFile stack;
try
{
    stack = new StackParser().Parse(entry.Data, entry.ResourceFork);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Parse failed: {ex.GetType().Name}: {ex.Message}");
    return 1;
}

Console.WriteLine($"Cards: {stack.Cards.Count}, Backgrounds: {stack.Backgrounds.Count}, Bitmaps: {stack.Bitmaps.Count}");
Console.WriteLine($"Card size: {stack.StackHeader.CardWidth}x{stack.StackHeader.CardHeight}");

var cardOrder = stack.GetCardOrder().ToList();
var cardsById = stack.Cards.ToDictionary(c => c.Header.Id);
var orderedCards = cardOrder.Count > 0
    ? cardOrder.Where(cardsById.ContainsKey).Select(id => cardsById[id]).ToList()
    : stack.Cards;

if (infoMode)
{
    PrintInventory(stack, orderedCards, maxCards);
    return 0;
}

Directory.CreateDirectory(outDir!);
var renderer = new CardRenderer(stack);
int n = 0;
foreach (var card in orderedCards)
{
    if (n >= maxCards) break;
    try
    {
        using var bmp = renderer.RenderCard(card);
        using var img = SKImage.FromBitmap(bmp);
        using var pngData = img.Encode(SKEncodedImageFormat.Png, 100);
        var outPath = Path.Combine(outDir!, $"card{n:D2}_{card.Header.Id}.png");
        using var fs = File.Create(outPath);
        pngData.SaveTo(fs);
        Console.WriteLine($"card {n} (id {card.Header.Id}): parts={card.Parts.Count} contents={card.PartContents.Count} bmap={card.BitmapId} -> {outPath}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"card {n} (id {card.Header.Id}) FAILED: {ex.GetType().Name}: {ex.Message}");
    }
    n++;
}

return 0;

static void PrintInventory(StackFile stack, IReadOnlyList<CardBlock> orderedCards, int maxCards)
{
    Console.WriteLine();
    Console.WriteLine("Block inventory:");
    Console.WriteLine(StackParser.GetBlockInventory(stack.Blocks));

    Console.WriteLine();
    Console.WriteLine("Card / part table:");
    int n = 0;
    foreach (var card in orderedCards)
    {
        if (n >= maxCards) break;
        Console.WriteLine($"Card {n} (id {card.Header.Id}, name \"{card.Name}\", bmap {card.BitmapId}):");
        foreach (var part in card.Parts)
        {
            Console.WriteLine(
                $"    part {part.PartId,-6} type={part.Type,-7} style={part.Style,-11} " +
                $"rect=({part.Left},{part.Top})-({part.Right},{part.Bottom}) " +
                $"icon={part.IconIdOrFirstSelectedLine,-5} scriptLen={part.Script.Length,-5} name=\"{part.Name}\"");
        }
        n++;
    }
}

static void PrintUsage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  RenderDump <input-file> <out-dir> [max-cards]   Render cards to PNG");
    Console.Error.WriteLine("  RenderDump --info <input-file> [max-cards]      Print block/part inventory only");
}
