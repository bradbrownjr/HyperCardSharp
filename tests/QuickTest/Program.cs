using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HyperCardSharp.Core.Containers;
using HyperCardSharp.Core.Parts;
using HyperCardSharp.Core.Resources;
using HyperCardSharp.Core.Stack;

void RunButtonDiag(string path)
{
    var raw = File.ReadAllBytes(path);
    Console.WriteLine($"\n=== {System.IO.Path.GetFileName(path)} ({raw.Length} bytes) ===");

    var entries = ContainerPipeline.UnwrapEntries(raw);
    var parser = new StackParser();

    foreach (var e in entries)
    {
        StackFile stack;
        try { stack = parser.Parse(e.Data, e.ResourceFork); }
        catch (Exception ex) { Console.WriteLine($"  '{e.Name}' parse error: {ex.Message}"); continue; }

        Console.WriteLine($"\n--- '{e.Name}' ({stack.StackHeader.CardWidth}x{stack.StackHeader.CardHeight}, {stack.Cards.Count} cards) ---");

        // All block types present in data fork
        var blockTypes = stack.Blocks.Select(b => b.Type).Distinct().OrderBy(t => t);
        Console.WriteLine($"  Block types: [{string.Join(", ", blockTypes)}]");

        // Resource fork icons
        if (e.ResourceFork != null)
        {
            var icons = MacResourceForkReader.GetResources(e.ResourceFork, "ICON");
            Console.WriteLine($"  Resource fork ICONs: [{string.Join(", ", icons.Keys)}]");
        }
        else Console.WriteLine("  Resource fork: none");

        // All card IDs in navigation order
        var cardOrder = stack.GetCardOrder().ToList();
        Console.WriteLine($"  Card IDs (first 10): [{string.Join(", ", cardOrder.Take(10))}]");

        // All icon IDs referenced by buttons across ALL cards
        var referencedIconIds = new SortedSet<int>();
        foreach (var card in stack.Cards)
        {
            foreach (var btn in card.Parts.Where(p => p.IsButton && p.IconId != 0))
                referencedIconIds.Add(btn.IconId);
        }
        foreach (var bg in stack.Backgrounds)
        {
            foreach (var btn in bg.Parts.Where(p => p.IsButton && p.IconId != 0))
                referencedIconIds.Add(btn.IconId);
        }
        Console.WriteLine($"  Icon IDs referenced by buttons: [{string.Join(", ", referencedIconIds)}]");

        // Check for navigability: what card IDs do scripts target?
        var targetIds = new SortedSet<int>();
        foreach (var card in stack.Cards)
        {
            foreach (var btn in card.Parts.Where(p => p.IsButton && !string.IsNullOrWhiteSpace(p.Script)))
            {
                var matches = System.Text.RegularExpressions.Regex.Matches(btn.Script, @"go to card id (\d+)");
                foreach (System.Text.RegularExpressions.Match m in matches)
                    targetIds.Add(int.Parse(m.Groups[1].Value));
            }
        }
        var missingIds = targetIds.Where(id => !cardOrder.Contains(id)).ToList();
        Console.WriteLine($"  Script 'go to card id' targets (sample): [{string.Join(", ", targetIds.Take(10))}]");
        if (missingIds.Count > 0)
            Console.WriteLine($"  *** MISSING card IDs (not in this stack): [{string.Join(", ", missingIds.Take(10))}...] ({missingIds.Count} total)");
        else
            Console.WriteLine("  All script targets resolvable within this stack.");
    }
}

var paths = args.Length > 0 ? args : new[]
{
    @"C:\Users\bradb\Nextcloud\Documents\GitHub\HyperCardSharp\samples\NEUROBLAST_Cyberdelia.sit",
    @"C:\Users\bradb\Nextcloud\Documents\GitHub\HyperCardSharp\samples\neuroblast.img",
};

foreach (var p in paths)
{
    if (File.Exists(p)) RunButtonDiag(p);
    else Console.WriteLine($"NOT FOUND: {p}");
}
