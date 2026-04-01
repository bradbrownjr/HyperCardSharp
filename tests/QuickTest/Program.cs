using System;
using System.IO;
using System.Linq;
using HyperCardSharp.Core.Containers;
using HyperCardSharp.Core.Resources;

void RunDiagnostic(string path)
{
    var raw = File.ReadAllBytes(path);
    Console.WriteLine($"\n=== {System.IO.Path.GetFileName(path)} ({raw.Length} bytes) ===");

    var entries = ContainerPipeline.UnwrapEntries(raw);
    Console.WriteLine($"Stacks found: {entries.Count}");
    foreach (var e in entries)
        Console.WriteLine($"  '{e.Name}': rsrc={(e.ResourceFork != null ? e.ResourceFork.Length + " bytes" : "null")}");

    foreach (var e in entries.Where(x => x.ResourceFork != null))
    {
        var icons = MacResourceForkReader.GetResources(e.ResourceFork!, "ICON");
        if (icons.Count == 0) continue;
        Console.WriteLine($"\n'{e.Name}' ICON resources: [{string.Join(", ", icons.Keys)}]");
        foreach (var (id, data) in icons.Take(3))
        {
            var px = MacResourceForkReader.DecodeIcon(data);
            if (px == null) continue;
            string row4  = string.Concat(Enumerable.Range(0, 32).Select(c => px[4  * 32 + c] ? "X" : "."));
            string row12 = string.Concat(Enumerable.Range(0, 32).Select(c => px[12 * 32 + c] ? "X" : "."));
            Console.WriteLine($"  ICON {id} row4:  {row4}");
            Console.WriteLine($"  ICON {id} row12: {row12}");
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
