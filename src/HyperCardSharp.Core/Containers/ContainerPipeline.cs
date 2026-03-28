namespace HyperCardSharp.Core.Containers;

/// <summary>
/// Chains container extractors: tries each extractor in order,
/// recursively re-detects the inner data after each successful extraction.
/// </summary>
public static class ContainerPipeline
{
    private static readonly IContainerExtractor[] Extractors =
    [
        new MacBinaryExtractor(),
        new AppleSingleExtractor(),
        new BinHexExtractor(),
        new StuffItExtractor(),
        new DiskCopyExtractor(),
        new HfsExtractor(),
        new RawStackScanner(),  // Last resort: scan raw data for embedded STAK blocks
    ];

    /// <summary>
    /// Attempt to extract a raw HyperCard stack from data that may be in a container.
    /// Returns the raw stack bytes on success, or null if a container was recognized but
    /// no stack could be extracted.
    /// If no container is recognized, returns the original data unchanged (may be a raw stack).
    /// </summary>
    /// <param name="data">Input bytes.</param>
    /// <param name="log">Optional logging callback — receives human-readable status messages.</param>
    /// <param name="depth">Recursion guard (internal use).</param>
    public static byte[]? Unwrap(byte[] data, Action<string>? log = null, int depth = 0)
    {
        if (depth > 5)
        {
            log?.Invoke("Container unwrap depth limit reached.");
            return data;
        }

        foreach (var extractor in Extractors)
        {
            bool canHandle;
            try { canHandle = extractor.CanHandle(data); }
            catch (Exception ex) { log?.Invoke($"{extractor.GetType().Name}.CanHandle threw: {ex.Message}"); continue; }

            if (!canHandle)
                continue;

            log?.Invoke($"Detected {extractor.GetType().Name} — extracting…");

            byte[]? extracted;
            try { extracted = extractor.Extract(data); }
            catch (Exception ex) { log?.Invoke($"{extractor.GetType().Name}.Extract threw: {ex.Message}"); return null; }

            if (extracted == null)
            {
                log?.Invoke($"{extractor.GetType().Name} recognized container but could not extract a stack.");
                return null;
            }

            log?.Invoke($"{extractor.GetType().Name} extracted {extracted.Length:N0} bytes — recursing.");
            return Unwrap(extracted, log, depth + 1) ?? extracted;
        }

        log?.Invoke($"No container format recognized ({data.Length:N0} bytes) — treating as raw stack.");
        return data;
    }

    /// <summary>
    /// Like UnwrapMultiple, but returns fully enriched StackEntry records
    /// with card count, size, and resolution metadata read from the STAK header.
    /// </summary>
    public static List<StackEntry> UnwrapEntries(byte[] data, Action<string>? log = null)
    {
        var tuples = UnwrapMultiple(data, log);
        var entries = new List<StackEntry>(tuples.Count);
        foreach (var (name, stackData) in tuples)
            entries.Add(StackEntry.FromRaw(name, stackData));
        return entries;
    }

    /// <summary>
    /// Like Unwrap, but when a disk image contains multiple STAK files, returns all of them.
    /// Each entry is (Name, Data) where Data is the recursively unwrapped stack bytes.
    /// Returns an empty list if no stacks could be extracted.
    /// </summary>
    public static List<(string Name, byte[] Data)> UnwrapMultiple(byte[] data, Action<string>? log = null, int depth = 0)
    {
        if (depth > 5)
        {
            log?.Invoke("Container unwrap depth limit reached.");
            return new List<(string Name, byte[] Data)> { ("stack", data) };
        }

        foreach (var extractor in Extractors)
        {
            bool canHandle;
            try { canHandle = extractor.CanHandle(data); }
            catch (Exception ex) { log?.Invoke($"{extractor.GetType().Name}.CanHandle threw: {ex.Message}"); continue; }

            if (!canHandle)
                continue;

            log?.Invoke($"Detected {extractor.GetType().Name} — extracting…");

            // StuffIt: enumerate all STAK entries before falling back to single-extract
            if (extractor is StuffItExtractor sit)
            {
                var sitStacks = sit.ExtractAll(data);
                if (sitStacks.Count == 0)
                {
                    log?.Invoke("StuffItExtractor found no STAK entries.");
                    return [];
                }
                log?.Invoke($"StuffItExtractor found {sitStacks.Count} stack(s).");
                var sitResults = new List<(string Name, byte[] Data)>();
                foreach (var (name, stackData) in sitStacks)
                {
                    var unwrapped = Unwrap(stackData, log, depth + 1) ?? stackData;
                    sitResults.Add((name, unwrapped));
                }
                return sitResults;
            }

            // Before RawStackScanner, try HFS+ enumeration for multi-stack support
            if (extractor is RawStackScanner)
            {
                var hfsPlus = new HfsPlusReader(data);
                if (hfsPlus.IsHfsPlus())
                {
                    var hfsPlusStacks = hfsPlus.EnumerateStacks();
                    if (hfsPlusStacks.Count > 0)
                    {
                        log?.Invoke($"HFS+ volume found with {hfsPlusStacks.Count} stack(s).");
                        return hfsPlusStacks;
                    }
                    log?.Invoke("HFS+ volume detected but no stacks found — falling back to raw scan.");
                }
                // fall through to existing RawStackScanner logic
            }

            // RawStackScanner: use ExtractAll to find multiple stacks
            if (extractor is RawStackScanner scanner)
            {
                var allStacks = scanner.ExtractAll(data);
                if (allStacks.Count == 0)
                {
                    log?.Invoke("RawStackScanner found no valid stacks.");
                    return [];
                }
                log?.Invoke($"RawStackScanner found {allStacks.Count} stack(s).");
                if (allStacks.Count > 1)
                    return allStacks;
                return new List<(string Name, byte[] Data)> { (allStacks[0].Name, allStacks[0].Data) };
            }

            byte[]? extracted;
            try { extracted = extractor.Extract(data); }
            catch (Exception ex) { log?.Invoke($"{extractor.GetType().Name}.Extract threw: {ex.Message}"); return []; }

            if (extracted == null)
            {
                log?.Invoke($"{extractor.GetType().Name} recognized container but could not extract a stack.");
                return [];
            }

            log?.Invoke($"{extractor.GetType().Name} extracted {extracted.Length:N0} bytes.");

            // After DiskCopy extraction, check if the HFS volume has multiple stacks
            if (extractor is DiskCopyExtractor)
            {
                var hfs = new HfsReader(extracted);
                if (hfs.IsHfs())
                {
                    var stacks = hfs.EnumerateStacks();
                    if (stacks.Count > 1)
                    {
                        log?.Invoke($"Found {stacks.Count} stacks in HFS volume.");
                        var results = new List<(string Name, byte[] Data)>();
                        foreach (var (name, stackData) in stacks)
                        {
                            var unwrapped = Unwrap(stackData, log, depth + 1) ?? stackData;
                            results.Add((name, unwrapped));
                        }
                        return results;
                    }
                    else if (stacks.Count == 1)
                    {
                        var unwrapped = Unwrap(stacks[0].Data, log, depth + 1) ?? stacks[0].Data;
                        return new List<(string Name, byte[] Data)> { (stacks[0].Name, unwrapped) };
                    }
                }
            }

            // For non-disk-image extractors or single-stack disk images, recurse
            return UnwrapMultiple(extracted, log, depth + 1);
        }

        log?.Invoke($"No container format recognized ({data.Length:N0} bytes) — treating as raw stack.");
        return new List<(string Name, byte[] Data)> { ("stack", data) };
    }
}
