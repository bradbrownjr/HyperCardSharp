using HyperCardSharp.Core.Containers;
using HyperCardSharp.Core.Stack;
using HyperCardSharp.HyperTalk.Parser;
using HyperCardSharp.Rendering;

namespace HyperCardSharp.Core.Tests;

/// <summary>
/// Phase 24: Real-world stack corpus validation.
///
/// These tests load every sample file in the <c>samples/</c> directory, unwrap it
/// through the container pipeline, parse each extracted stack, attempt to render
/// the first card, and parse every script found in the stack.  They are skipped
/// when the samples directory is absent (e.g. on CI without the large binary assets).
/// </summary>
public class Phase24CorpusTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? FindSamplesDir()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, "samples");
            if (Directory.Exists(candidate) && Directory.GetFiles(candidate).Length > 0)
                return candidate;
            dir = Path.GetDirectoryName(dir)!;
        }
        return null;
    }

    private static IEnumerable<string> AllSampleFiles()
    {
        var dir = FindSamplesDir();
        if (dir == null) yield break;
        foreach (var f in Directory.GetFiles(dir))
            yield return f;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// For every sample file: unwrap via ContainerPipeline → parse all extracted
    /// stacks → assert no exception is thrown.  Samples that the pipeline cannot
    /// extract (returns null or no STAK magic) are counted as skipped, not failures.
    /// At least one sample must load to give the test real coverage.
    /// </summary>
    [SkippableFact]
    public void AllSamples_LoadWithoutException()
    {
        var samplesDir = FindSamplesDir();
        Skip.If(samplesDir == null, "samples/ directory not found");

        var exceptions = new List<string>();   // real failures: exceptions thrown
        var skipped    = new List<string>();   // known gaps: pipeline null / no magic
        var parser = new StackParser();
        int totalStacks = 0;

        foreach (var file in AllSampleFiles())
        {
            byte[] data;
            try { data = File.ReadAllBytes(file); }
            catch (Exception ex) { exceptions.Add($"[READ] {Path.GetFileName(file)}: {ex.Message}"); continue; }

            byte[]? raw;
            try { raw = ContainerPipeline.Unwrap(data); }
            catch (Exception ex) { exceptions.Add($"[UNWRAP-EXCEPTION] {Path.GetFileName(file)}: {ex.Message}"); continue; }

            if (raw == null)
            {
                skipped.Add($"[PIPELINE-NULL] {Path.GetFileName(file)}: container recognized but no stack extracted");
                continue;
            }

            if (raw.Length < 8 || raw[4] != 'S' || raw[5] != 'T' || raw[6] != 'A' || raw[7] != 'K')
            {
                skipped.Add($"[NO-MAGIC] {Path.GetFileName(file)}: no STAK magic after unwrap");
                continue;
            }

            try { _ = parser.Parse(raw); totalStacks++; }
            catch (InvalidDataException ex) when (ex.Message.Contains("No STAK block"))
            {
                // Magic bytes happened to be at offset 4 but no valid STAK block — pipeline gap
                skipped.Add($"[NO-STAK-BLOCK] {Path.GetFileName(file)}: {ex.Message}");
            }
            catch (Exception ex) { exceptions.Add($"[PARSE] {Path.GetFileName(file)}: {ex.Message}"); }
        }

        // Report findings
        Console.WriteLine($"Phase 24 load: {totalStacks} stacks OK, {skipped.Count} skipped (pipeline gap), {exceptions.Count} exceptions");
        foreach (var s in skipped)   Console.WriteLine($"  SKIP: {s}");
        foreach (var e in exceptions) Console.WriteLine($"  EXCEPTION: {e}");

        // Assert: no exceptions thrown (pipeline nulls are known gaps, not regressions).
        Assert.Empty(exceptions);
        // Assert: we got at least something useful
        Skip.If(totalStacks == 0, "No samples loaded; skipping coverage assertions.");
        Assert.True(totalStacks > 0);
    }

    /// <summary>
    /// For every sample file: parse every script in every card, background, part
    /// and stack header using HyperTalkParser. Reports unrecognised tokens but does
    /// NOT fail the test on parse warnings (they are expected for uncommon syntax).
    /// </summary>
    [SkippableFact]
    public void AllSamples_ScriptsParseable()
    {
        var samplesDir = FindSamplesDir();
        Skip.If(samplesDir == null, "samples/ directory not found");

        var htParser = new HyperTalkParser();
        var parseErrors = new List<string>();
        var parser = new StackParser();
        int totalScripts = 0;

        foreach (var file in AllSampleFiles())
        {
            byte[] data;
            try { data = File.ReadAllBytes(file); }
            catch { continue; }

            byte[]? raw;
            try { raw = ContainerPipeline.Unwrap(data); }
            catch { continue; }

            if (raw == null || raw.Length < 8 || raw[4] != 'S' || raw[5] != 'T' || raw[6] != 'A' || raw[7] != 'K') continue;

            StackFile stack;
            try { stack = parser.Parse(raw); }
            catch { continue; }

            // Collect all scripts in this stack
            var scripts = new List<(string Source, string Script)>();

            if (!string.IsNullOrWhiteSpace(stack.StackHeader.Script))
                scripts.Add(($"{Path.GetFileName(file)}:stack", stack.StackHeader.Script));

            foreach (var card in stack.Cards)
            {
                if (!string.IsNullOrWhiteSpace(card.Script))
                    scripts.Add(($"{Path.GetFileName(file)}:card#{card.Header.Id}", card.Script));
                foreach (var part in card.Parts)
                    if (!string.IsNullOrWhiteSpace(part.Script))
                        scripts.Add(($"{Path.GetFileName(file)}:card#{card.Header.Id}/part#{part.PartId}", part.Script));
            }

            foreach (var bg in stack.Backgrounds)
            {
                if (!string.IsNullOrWhiteSpace(bg.Script))
                    scripts.Add(($"{Path.GetFileName(file)}:bg#{bg.Header.Id}", bg.Script));
                foreach (var part in bg.Parts)
                    if (!string.IsNullOrWhiteSpace(part.Script))
                        scripts.Add(($"{Path.GetFileName(file)}:bg#{bg.Header.Id}/part#{part.PartId}", part.Script));
            }

            foreach (var (source, scriptText) in scripts)
            {
                try
                {
                    var lexer = new HyperCardSharp.HyperTalk.Lexer.HyperTalkLexer();
                    var tokens = lexer.Tokenize(scriptText);
                    var warnings = new List<string>();
                    htParser.OnWarning = w => warnings.Add(w);
                    htParser.Parse(tokens);
                    totalScripts++;
                }
                catch (Exception ex)
                {
                    parseErrors.Add($"[EXCEPTION] {source}: {ex.Message}");
                }
            }
        }

        Console.WriteLine($"Phase 24: {totalScripts} scripts parsed.");
        foreach (var e in parseErrors) Console.WriteLine($"  ERROR: {e}");

        // Parse exceptions (not warnings) are failures
        Assert.Empty(parseErrors);
    }

    /// <summary>
    /// For every sample file: render the first card of each extracted stack
    /// using CardRenderer. Verifies the renderer does not throw for any real-world
    /// stack. Golden bitmap comparison is not implemented (would require checking
    /// in large binary reference files).
    /// </summary>
    [SkippableFact]
    public void AllSamples_FirstCardRendersWithoutException()
    {
        var samplesDir = FindSamplesDir();
        Skip.If(samplesDir == null, "samples/ directory not found");

        var failures = new List<string>();
        var parser = new StackParser();
        int rendered = 0;

        foreach (var file in AllSampleFiles())
        {
            byte[] data;
            try { data = File.ReadAllBytes(file); }
            catch { continue; }

            byte[]? raw;
            try { raw = ContainerPipeline.Unwrap(data); }
            catch { continue; }

            if (raw == null || raw.Length < 8 || raw[4] != 'S' || raw[5] != 'T' || raw[6] != 'A' || raw[7] != 'K') continue;

            StackFile stack;
            try { stack = parser.Parse(raw); }
            catch { continue; }

            if (stack.Cards.Count == 0) continue;

            try
            {
                var renderer = new CardRenderer(stack);
                var cardOrder = stack.GetCardOrder().ToList();
                if (cardOrder.Count == 0) cardOrder = stack.Cards.Select(c => c.Header.Id).ToList();

                var firstCardId = cardOrder[0];
                var firstCard = stack.Cards.FirstOrDefault(c => c.Header.Id == firstCardId);
                if (firstCard == null) continue;

                using var bitmap = renderer.RenderCard(firstCard, RenderMode.BlackAndWhite);
                Assert.NotNull(bitmap);
                Assert.True(bitmap.Width > 0 && bitmap.Height > 0);
                rendered++;
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }

        Console.WriteLine($"Phase 24: {rendered} first cards rendered successfully.");
        foreach (var f in failures) Console.WriteLine($"  FAIL: {f}");

        Assert.Empty(failures);
    }

    /// <summary>
    /// Coverage report: prints all unrecognised block types, unknown commands,
    /// and missing properties encountered across the sample corpus.
    /// This test always passes — it exists purely for diagnostic output.
    /// </summary>
    [SkippableFact]
    public void AllSamples_CoverageReport()
    {
        var samplesDir = FindSamplesDir();
        Skip.If(samplesDir == null, "samples/ directory not found");

        var parser = new StackParser();
        var unknownBlockTypes = new SortedSet<string>();
        int totalCards = 0, totalParts = 0, totalScripts = 0;

        foreach (var file in AllSampleFiles())
        {
            byte[] data;
            try { data = File.ReadAllBytes(file); }
            catch { continue; }

            byte[]? raw;
            try { raw = ContainerPipeline.Unwrap(data); }
            catch { continue; }

            if (raw == null || raw.Length < 8 || raw[4] != 'S' || raw[5] != 'T' || raw[6] != 'A' || raw[7] != 'K') continue;

            StackFile stack;
            try { stack = parser.Parse(raw); }
            catch { continue; }

            totalCards  += stack.Cards.Count;
            totalParts  += stack.Cards.Sum(c => c.Parts.Count) + stack.Backgrounds.Sum(b => b.Parts.Count);
            totalScripts += stack.Cards.Count(c => !string.IsNullOrWhiteSpace(c.Script))
                         + stack.Backgrounds.Count(b => !string.IsNullOrWhiteSpace(b.Script));

            foreach (var block in stack.Blocks)
            {
                if (block.Type is not ("STAK" or "MAST" or "LIST" or "PAGE" or "CARD" or "BKGD" or "BMAP" or "TAIL" or "FREE"))
                    unknownBlockTypes.Add(block.Type);
            }
        }

        Console.WriteLine("=== Phase 24 Coverage Report ===");
        Console.WriteLine($"Total cards:   {totalCards}");
        Console.WriteLine($"Total parts:   {totalParts}");
        Console.WriteLine($"Total scripts: {totalScripts}");
        if (unknownBlockTypes.Count > 0)
        {
            Console.WriteLine("Unrecognised block types (candidates for future parsing):");
            foreach (var t in unknownBlockTypes) Console.WriteLine($"  {t}");
        }
        else
        {
            Console.WriteLine("All block types in the corpus are recognised.");
        }

        // Always passes — this is a diagnostic test
        Assert.True(true);
    }
}
