using System;
using System.Collections.Generic;
using System.IO;
using HyperCardSharp.Core.Containers;

var path = args[0];
var data = File.ReadAllBytes(path);
Console.WriteLine($"Input: {path} ({data.Length} bytes)");

var logLines = new List<string>();
var stacks = ContainerPipeline.UnwrapMultiple(data, msg => logLines.Add(msg));

Console.WriteLine($"Found {stacks.Count} stack(s):");
for (int i = 0; i < stacks.Count; i++)
    Console.WriteLine($"  [{i}] \"{stacks[i].Name}\"");
