using HyperCardSharp.Core.Parts;
using HyperCardSharp.Core.Stack;

namespace HyperCardSharp.Core.Tests;

public class Phase2ParserTests
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
    public void Parse_Cards_HasExpectedCount()
    {
        var stack = LoadStack();
        Skip.If(stack == null, "Sample not found");

        Assert.Equal(70, stack!.Cards.Count);
    }

    [SkippableFact]
    public void Parse_Cards_AllHaveValidBackgroundId()
    {
        var stack = LoadStack();
        Skip.If(stack == null, "Sample not found");

        var bgIds = stack!.Backgrounds.Select(b => b.Header.Id).ToHashSet();
        foreach (var card in stack.Cards)
        {
            Assert.Contains(card.BackgroundId, bgIds);
        }
    }

    [SkippableFact]
    public void Parse_FirstCard_HasButtonPart()
    {
        var stack = LoadStack();
        Skip.If(stack == null, "Sample not found");

        var firstCard = stack!.Cards[0];
        Assert.Single(firstCard.Parts);

        var button = firstCard.Parts[0];
        Assert.Equal(PartType.Button, button.Type);
        Assert.Equal("Yes,  please!", button.Name);
        Assert.Contains("on mouseUp", button.Script);
        Assert.Contains("visual effect", button.Script);
    }

    [SkippableFact]
    public void Parse_FirstCard_HasBitmap()
    {
        var stack = LoadStack();
        Skip.If(stack == null, "Sample not found");

        // First card should reference a BMAP block
        Assert.NotEqual(0, stack!.Cards[0].BitmapId);
    }

    [SkippableFact]
    public void Parse_Background_HasExpectedCardCount()
    {
        var stack = LoadStack();
        Skip.If(stack == null, "Sample not found");

        Assert.Single(stack!.Backgrounds);
        var bg = stack.Backgrounds[0];
        Assert.Equal(70, bg.CardCount);
        Assert.Equal(2777, bg.Header.Id);
    }

    [SkippableFact]
    public void Parse_Background_LinkedListWrapsAround()
    {
        var stack = LoadStack();
        Skip.If(stack == null, "Sample not found");

        // Single background should point to itself for next/prev
        var bg = stack!.Backgrounds[0];
        Assert.Equal(bg.Header.Id, bg.NextBackgroundId);
        Assert.Equal(bg.Header.Id, bg.PrevBackgroundId);
    }

    [SkippableFact]
    public void Parse_ListBlock_HasCorrectTotals()
    {
        var stack = LoadStack();
        Skip.If(stack == null, "Sample not found");

        Assert.NotNull(stack!.ListIndex);
        Assert.Equal(1, stack.ListIndex!.PageCount);
        Assert.Equal(70, stack.ListIndex.TotalCardCount);
        Assert.True(stack.ListIndex.CardReferenceSize > 0);
    }

    [SkippableFact]
    public void Parse_PageBlock_Has70CardReferences()
    {
        var stack = LoadStack();
        Skip.If(stack == null, "Sample not found");

        Assert.Single(stack!.Pages);
        var page = stack.Pages[0];
        Assert.Equal(70, page.CardReferences.Count);

        // All card IDs should be non-zero
        foreach (var cardRef in page.CardReferences)
            Assert.NotEqual(0, cardRef.CardId);
    }

    [SkippableFact]
    public void Parse_CardOrder_MatchesCardBlocks()
    {
        var stack = LoadStack();
        Skip.If(stack == null, "Sample not found");

        // Every card ID in the page references should correspond to a CARD block
        var cardBlockIds = stack!.Cards.Select(c => c.Header.Id).ToHashSet();
        var orderedIds = stack.GetCardOrder().ToList();

        Assert.Equal(70, orderedIds.Count);
        foreach (var id in orderedIds)
            Assert.Contains(id, cardBlockIds);
    }

    [SkippableFact]
    public void Parse_FontTable_Has22Fonts()
    {
        var stack = LoadStack();
        Skip.If(stack == null, "Sample not found");

        Assert.NotNull(stack!.FontTable);
        Assert.Equal(22, stack.FontTable!.FontCount);
        Assert.Equal(22, stack.FontTable.Fonts.Count);

        // Check some known fonts
        var fontNames = stack.FontTable.Fonts.Select(f => f.Name).ToList();
        Assert.Contains("Chicago", fontNames);
        Assert.Contains("Geneva", fontNames);
        Assert.Contains("Courier", fontNames);
    }

    [SkippableFact]
    public void Parse_AllCardParts_HaveValidRects()
    {
        var stack = LoadStack();
        Skip.If(stack == null, "Sample not found");

        int totalParts = 0;
        foreach (var card in stack!.Cards)
        {
            foreach (var part in card.Parts)
            {
                Assert.True(part.Bottom >= part.Top, $"Part {part.PartId}: bottom < top");
                Assert.True(part.Right >= part.Left, $"Part {part.PartId}: right < left");
                Assert.True(part.Width <= 640, $"Part {part.PartId}: width {part.Width} > 640");
                Assert.True(part.Height <= 400, $"Part {part.PartId}: height {part.Height} > 400");
                totalParts++;
            }
        }

        Console.WriteLine($"Total card parts parsed: {totalParts}");
        Assert.True(totalParts > 0, "Should have at least one part across all cards");
    }

    [SkippableFact]
    public void Parse_PrintSummary()
    {
        var stack = LoadStack();
        Skip.If(stack == null, "Sample not found");

        Console.WriteLine($"Cards: {stack!.Cards.Count}");
        Console.WriteLine($"Backgrounds: {stack.Backgrounds.Count}");
        Console.WriteLine($"Pages: {stack.Pages.Count}");
        Console.WriteLine($"Fonts: {stack.FontTable?.FontCount ?? 0}");
        Console.WriteLine($"Styles: {stack.StyleTable?.StyleCount ?? 0}");

        var totalCardParts = stack.Cards.Sum(c => c.Parts.Count);
        var totalBgParts = stack.Backgrounds.Sum(b => b.Parts.Count);
        var cardsWithScripts = stack.Cards.Count(c => !string.IsNullOrEmpty(c.Script));
        var partsWithScripts = stack.Cards.SelectMany(c => c.Parts).Count(p => !string.IsNullOrEmpty(p.Script));

        Console.WriteLine($"Card parts: {totalCardParts}, Background parts: {totalBgParts}");
        Console.WriteLine($"Cards with scripts: {cardsWithScripts}");
        Console.WriteLine($"Parts with scripts: {partsWithScripts}");

        // Print first few font entries
        if (stack.FontTable != null)
        {
            Console.WriteLine("\nFont table:");
            foreach (var font in stack.FontTable.Fonts)
                Console.WriteLine($"  #{font.FontId}: {font.Name}");
        }

        // Print first 5 cards with their parts
        Console.WriteLine("\nFirst 5 cards:");
        foreach (var card in stack.Cards.Take(5))
        {
            Console.WriteLine($"  Card {card.Header.Id}: bitmap={card.BitmapId}, bg={card.BackgroundId}, " +
                $"parts={card.Parts.Count}, name=\"{card.Name}\"");
            foreach (var part in card.Parts)
            {
                Console.WriteLine($"    {part.Type} #{part.PartId} \"{part.Name}\" " +
                    $"({part.Left},{part.Top},{part.Right},{part.Bottom}) style={part.Style}" +
                    (string.IsNullOrEmpty(part.Script) ? "" : $" [has script: {part.Script.Length} chars]"));
            }
        }
    }
}
