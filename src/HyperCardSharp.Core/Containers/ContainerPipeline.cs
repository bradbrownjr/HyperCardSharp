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
            var hfsForks = TryExtractHfsResourceForks(data);
            // "*" is the sentinel key for the volume-wide fallback — promote to commonRsrcFork.
            if (hfsForks.TryGetValue("*", out var volumeFork))
                commonRsrcFork = volumeFork;
            else if (hfsForks.Count > 0)
                namedRsrcForks = hfsForks;

            // MacBinary: the file itself carries a resource fork alongside the data fork.
            if (commonRsrcFork == null && namedRsrcForks == null)
            {
                var mbForks = new MacBinaryExtractor().ExtractForks(data);
                if (mbForks?.ResourceFork?.Length > 0)
                    commonRsrcFork = mbForks.Value.ResourceFork;
            }

            // AppleSingle/AppleDouble: same idea.
            if (commonRsrcFork == null && namedRsrcForks == null)
            {
                var asForks = new AppleSingleExtractor().ExtractForks(data);
                if (asForks?.ResourceFork?.Length > 0)
                    commonRsrcFork = asForks.Value.ResourceFork;
            }
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
    ///
    /// If the STAK files themselves carry no resource fork (rsrcLogEof == 0), falls back
    /// to scanning ALL files on the volume looking for one that contains ICON resources.
    /// This handles disc releases where icons are stored in a companion file rather than
    /// embedded in the stack file's resource fork.
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
                    if (r.IsHfs())
                    {
                        var forks = r.EnumerateResourceForks();
                        if (forks.Count > 0) return forks;
                        return TryVolumeWideForkFallback(r);
                    }
                }
            }

            // 2. Try the data as a raw HFS volume at offset 0 (handles images where the
            //    HFS volume starts at the very beginning of the file).
            {
                var reader = new HfsReader(data);
                if (reader.IsHfs())
                {
                    var forks = reader.EnumerateResourceForks();
                    if (forks.Count > 0) return forks;
                    return TryVolumeWideForkFallback(reader);
                }
            }

            // 3. Scan for a partitioned disk image where the HFS volume starts at a
            //    non-zero 512-byte-aligned sector offset (e.g., Apple SCSI hard-disk).
            //    Pre-filter with a cheap span read before allocating a slice array.
            const int sector = 512;
            const int mdbRelOffset = 2 * sector; // HFS MDB is at volume-relative offset 1024
            const ushort hfsSig = 0xD2D7;
            const uint tsMin = 0xA8000000u;
            const uint tsMax = 0xF8000000u;
            var span = data.AsSpan();

            for (int vStart = sector; vStart + mdbRelOffset + 10 <= data.Length; vStart += sector)
            {
                // Cheap pre-check on the candidate MDB offset before allocating.
                ushort sig = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(span.Slice(vStart + mdbRelOffset, 2));
                if (sig != hfsSig)
                {
                    uint crDate = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(span.Slice(vStart + mdbRelOffset + 2, 4));
                    if (crDate < tsMin || crDate > tsMax) continue;
                }

                byte[] hfsSlice = data.AsSpan(vStart).ToArray();
                var reader = new HfsReader(hfsSlice);
                if (!reader.IsHfs()) continue;

                var forks = reader.EnumerateResourceForks();
                if (forks.Count > 0) return forks;

                // STAK files have empty resource forks — scan all other files on the
                // volume for ICON-bearing resource forks to use as a shared pool.
                return TryVolumeWideForkFallback(reader);
            }
        }
        catch
        {
            // Gracefully degrade — icons just won't be available.
        }

        return new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Scans every file on the HFS volume for a resource fork that contains ICON resources.
    /// Returns the first such fork keyed under the special entry "*" so that
    /// <see cref="UnwrapEntries"/> can distribute it to all extracted stacks.
    /// </summary>
    private static Dictionary<string, byte[]> TryVolumeWideForkFallback(HfsReader reader)
    {
        var allForks = reader.EnumerateAllResourceForks();
        foreach (var (_, fork) in allForks)
        {
            var icons = HyperCardSharp.Core.Resources.MacResourceForkReader.GetResources(fork, "ICON");
            if (icons.Count > 0)
                return new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) { ["*"] = fork };
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

            // HfsExtractor: enumerate all STAK files in the volume.
            if (extractor is HfsExtractor hfsExt)
            {
                var hfsReader = new HfsReader(data);
                if (hfsReader.IsHfs())
                {
                    var hfsStacks = hfsReader.EnumerateStacks();
                    if (hfsStacks.Count > 0)
                    {
                        log?.Invoke($"HFS volume found with {hfsStacks.Count} stack(s).");
                        return hfsStacks;
                    }
                    log?.Invoke("HFS volume detected but no STAK files found.");
                }
                // If HFS but no stacks found, fall through to next extractor
                continue;
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
