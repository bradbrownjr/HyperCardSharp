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

void RunHfsDiag(string path)
{
    var raw = File.ReadAllBytes(path);
    Console.WriteLine($"\n=== HFS VOLUME SCAN: {System.IO.Path.GetFileName(path)} ({raw.Length} bytes) ===");
    Console.WriteLine($"  Bytes 0-7: {string.Join(" ", raw.Take(8).Select(b => b.ToString("X2")))}");

    // Check for HFS MDB signature at standard offset
    if (raw.Length >= 1026)
    {
        ushort sig = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(raw.AsSpan(1024, 2));
        Console.WriteLine($"  Signature at offset 1024: 0x{sig:X4} ({(sig == 0xD2D7 ? "HFS MDB ✓" : "not D2D7")})");
    }

    var reader = new HyperCardSharp.Core.Containers.HfsReader(raw);
    Console.WriteLine($"  IsHfs(): {reader.IsHfs()}");

    if (reader.IsHfs())
    {
        Console.WriteLine("  --- STAK resource forks (per-file): ---");
        var stakForks = reader.EnumerateResourceForks();
        if (stakForks.Count == 0)
            Console.WriteLine("    (none — STAK files have no resource fork)");
        foreach (var (name, fork) in stakForks)
        {
            var icons = HyperCardSharp.Core.Resources.MacResourceForkReader.GetResources(fork, "ICON");
            Console.WriteLine($"    '{name}': {fork.Length} bytes, {icons.Count} ICON(s): [{string.Join(", ", icons.Keys)}]");
        }

        Console.WriteLine("  --- ALL file resource forks on volume: ---");
        var allForks = reader.EnumerateAllResourceForks();
        if (allForks.Count == 0)
            Console.WriteLine("    (no files on volume have any resource fork data)");
        foreach (var (name, fork) in allForks.OrderByDescending(kv => kv.Value.Length))
        {
            var icons = HyperCardSharp.Core.Resources.MacResourceForkReader.GetResources(fork, "ICON");
            Console.WriteLine($"    '{name}': {fork.Length} bytes rsrc fork, {icons.Count} ICON(s): [{string.Join(", ", icons.Keys)}]");
        }
    }
}

var paths = args.Length > 0 ? args : new[]
{
    @"C:\Users\bradb\Nextcloud\Documents\GitHub\HyperCardSharp\samples\NEUROBLAST_Cyberdelia.sit",
    @"C:\Users\bradb\Nextcloud\Documents\GitHub\HyperCardSharp\samples\neuroblast.img",
};

foreach (var p in paths)
{
    if (!File.Exists(p)) { Console.WriteLine($"NOT FOUND: {p}"); continue; }
    RunButtonDiag(p);
    // For .img files also show the raw HFS volume contents
    if (p.EndsWith(".img", StringComparison.OrdinalIgnoreCase))
        RunHfsDiag(p);
}
