using System;
using System.IO;
using System.Linq;
using HyperCardSharp.Core.Containers;
using HyperCardSharp.Core.Resources;
using HyperCardSharp.Core.Stack;

void RunDiagnostic(string path)
{
    var raw = File.ReadAllBytes(path);
    Console.WriteLine($"\n=== {System.IO.Path.GetFileName(path)} ({raw.Length} bytes) ===");

    var entries = ContainerPipeline.UnwrapEntries(raw);
    Console.WriteLine($"Stacks found: {entries.Count}");
    foreach (var e in entries)
        Console.WriteLine($"  '{e.Name}': rsrc={(e.ResourceFork != null ? e.ResourceFork.Length + " bytes" : "null")}");

    // Print card dimensions for each stack
    var parser = new StackParser();
    foreach (var e in entries)
    {
        try
        {
            var stack = parser.Parse(e.Data, e.ResourceFork);
            Console.WriteLine($"  '{e.Name}': CardWidth={stack.StackHeader.CardWidth}, CardHeight={stack.StackHeader.CardHeight}, Cards={stack.Cards.Count}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  '{e.Name}': parse error: {ex.Message}");
        }
    }

    foreach (var e in entries.Where(x => x.ResourceFork != null))
    {
        var icons = MacResourceForkReader.GetResources(e.ResourceFork!, "ICON");
        if (icons.Count == 0) continue;
        Console.WriteLine($"\n'{e.Name}' ICON resources: [{string.Join(", ", icons.Keys)}]");
        foreach (var (id, data) in icons.Take(3))
        {
            var px = MacResourceForkReader.DecodeIcon(data);
            if (px == null) continue;
            // Print full icon as ASCII art
            Console.WriteLine($"  ICON {id} (32x32):");
            for (int row = 0; row < 32; row++)
            {
                string line = string.Concat(Enumerable.Range(0, 32).Select(c => px[row * 32 + c] ? "X" : "."));
                Console.WriteLine($"    {row,2}: {line}");
            }
        }
    }
}

// Run against all provided paths, or default to the .sit sample
var paths = args.Length > 0 ? args : new[]
{
    @"C:\Users\bradb\Nextcloud\Documents\GitHub\HyperCardSharp\samples\NEUROBLAST_Cyberdelia.sit",
    @"C:\Users\bradb\Nextcloud\Documents\GitHub\HyperCardSharp\samples\neuroblast.img",
};

foreach (var p in paths)
{
    if (File.Exists(p))
        RunDiagnostic(p);
    else
        Console.WriteLine($"NOT FOUND: {p}");
}
