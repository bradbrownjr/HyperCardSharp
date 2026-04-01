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
    /// Resource forks are attached from StuffIt archives or HFS images, enabling
    /// ICON resource extraction.
    /// </summary>
    public static List<StackEntry> UnwrapEntries(byte[] data, Action<string>? log = null)
    {
        var tuples = UnwrapMultiple(data, log);

        // ── Attach resource forks ─────────────────────────────────────────────
        // StuffIt: a single archive entry may contain multiple stacks. Its one
        // resource fork is shared across all extracted stacks. Attach it to all
        // entries that don't already have an individually-named match.
        // HFS: each stack file has its own resource fork keyed by filename.
        var sit = new StuffItExtractor();
        byte[]? commonRsrcFork = null;
        Dictionary<string, byte[]>? namedRsrcForks = null;

        if (sit.CanHandle(data))
        {
            var sitForks = sit.ExtractAllResourceForks(data);
            if (sitForks.Count == 1)
            {
                // Single archive entry → share its resource fork with every extracted stack.
                commonRsrcFork = sitForks.Values.First();
            }
            else if (sitForks.Count > 1)
            {
                namedRsrcForks = sitForks;
            }
        }
        else
        {
            namedRsrcForks = TryExtractHfsResourceForks(data);
        }

        var entries = new List<StackEntry>(tuples.Count);
        foreach (var (name, stackData) in tuples)
        {
            byte[]? rsrcFork = commonRsrcFork;
            if (rsrcFork == null)
                namedRsrcForks?.TryGetValue(name, out rsrcFork);
            entries.Add(StackEntry.FromRaw(name, stackData, rsrcFork));
        }
        return entries;
    }

    /// <summary>
    /// Attempt to extract resource forks for all STAK files in an HFS image.
    /// Handles:
    ///   - DiskCopy 4.2 → raw HFS volume
    ///   - Raw HFS volume at offset 0
    ///   - Partitioned disk images where the HFS volume starts at a non-zero
    ///     sector-aligned offset (e.g., Apple SCSI hard-disk images)
    /// </summary>
    private static Dictionary<string, byte[]> TryExtractHfsResourceForks(byte[] data)
    {
        try
        {
            // 1. DiskCopy → HFS
            var dc = new DiskCopyExtractor();
            if (dc.CanHandle(data))
            {
                var hfsData = dc.Extract(data);
                if (hfsData != null)
                {
                    var r = new HfsReader(hfsData);
                    if (r.IsHfs()) return r.EnumerateResourceForks();
                }
            }

            // 2. Scan for classic HFS at any 512-byte-aligned partition offset.
            //    The HFS Master Directory Block (MDB) signature 0xD2D7 lives at
            //    offset +1024 from the start of the HFS volume.
            const int sector = 512;
            const int mdbRelOffset = 2 * sector; // = 1024
            const ushort hfsSig = 0xD2D7;
            var span = data.AsSpan();

            for (int vStart = 0; vStart + mdbRelOffset + 2 <= data.Length; vStart += sector)
            {
                if (System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(span.Slice(vStart + mdbRelOffset, 2)) != hfsSig)
                    continue;

                // Slice from the volume start so HfsReader sees MDB at offset 1024.
                byte[] hfsSlice = vStart == 0 ? data : data.AsSpan(vStart).ToArray();
                var reader = new HfsReader(hfsSlice);
                if (reader.IsHfs())
                    return reader.EnumerateResourceForks();
            }
        }
        catch
        {
            // Gracefully degrade — icons just won't be available.
        }

        return new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
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
