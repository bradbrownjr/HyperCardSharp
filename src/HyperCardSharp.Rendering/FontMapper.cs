using System.Reflection;
using HyperCardSharp.Core.Parts;
using HyperCardSharp.Core.Stack;
using SkiaSharp;

namespace HyperCardSharp.Rendering;

/// <summary>
/// Maps classic Macintosh font IDs to SkiaSharp typefaces using a multi-tier
/// resolution strategy:
///
///   1. User-supplied font directory  — drop original .ttf/.otf files next to the app
///   2. System-installed fonts        — e.g. Geneva.ttf on macOS, or user-installed on Windows/Linux
///   3. Embedded open-source substitutes — ChicagoFLF (MIT), Noto Sans (SIL OFL)
///   4. Common cross-platform fonts   — Arial, Times New Roman, Courier New
///   5. SKTypeface.Default            — absolute last resort
///
/// See docs/fonts.md for details on supplying original Mac fonts for best fidelity.
/// </summary>
public static class FontMapper
{
    // ── Embedded typefaces (tier 3) ──────────────────────────────────────────

    // ChicagoFLF — loaded once from the embedded resource.
    private static readonly Lazy<SKTypeface?> s_chicagoFlf = new(LoadChicagoFlf);

    private static SKTypeface? LoadChicagoFlf()
    {
        var asm = typeof(FontMapper).Assembly;
        using var stream = asm.GetManifestResourceStream(
            "HyperCardSharp.Rendering.Assets.ChicagoFLF.ttf");
        return stream != null ? SKTypeface.FromStream(stream) : null;
    }

    // Noto Sans Regular/Bold — free (SIL OFL) Geneva substitute.
    private static readonly Lazy<SKTypeface?> s_notoRegular = new(() => LoadEmbeddedTypeface("NotoSans-Regular.ttf"));
    private static readonly Lazy<SKTypeface?> s_notoBold    = new(() => LoadEmbeddedTypeface("NotoSans-Bold.ttf"));

    private static SKTypeface? LoadEmbeddedTypeface(string fileName)
    {
        var asm = typeof(FontMapper).Assembly;
        using var stream = asm.GetManifestResourceStream(
            $"HyperCardSharp.Rendering.Assets.{fileName}");
        return stream != null ? SKTypeface.FromStream(stream) : null;
    }

    // ── User font directory (tier 1) ────────────────────────────────────────

    /// <summary>
    /// Optional path to a directory containing user-supplied .ttf/.otf font files.
    /// Set this before rendering to enable tier-1 font resolution.
    /// Typically the "fonts" folder next to the application or next to the loaded stack.
    /// </summary>
    public static string? UserFontDirectory { get; set; }

    // Cache of typefaces loaded from the user font directory, keyed by lowercase family name.
    private static readonly Dictionary<string, SKTypeface> s_userFonts = new(StringComparer.OrdinalIgnoreCase);
    private static bool s_userFontsScanned;

    private static void EnsureUserFontsScanned()
    {
        if (s_userFontsScanned) return;
        s_userFontsScanned = true;
        ScanUserFontDirectory();
    }

    /// <summary>Forces a re-scan of the user font directory (e.g. after changing the path).</summary>
    public static void RescanUserFonts()
    {
        s_userFontsScanned = false;
        foreach (var tf in s_userFonts.Values)
            tf.Dispose();
        s_userFonts.Clear();
        EnsureUserFontsScanned();
    }

    private static void ScanUserFontDirectory()
    {
        if (string.IsNullOrEmpty(UserFontDirectory) || !Directory.Exists(UserFontDirectory))
            return;

        foreach (var file in Directory.EnumerateFiles(UserFontDirectory, "*.*"))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext != ".ttf" && ext != ".otf") continue;

            var tf = SKTypeface.FromFile(file);
            if (tf == null) continue;

            // Index by the font's internal family name (what SkiaSharp reports).
            var familyName = tf.FamilyName;
            if (!string.IsNullOrEmpty(familyName) && !s_userFonts.ContainsKey(familyName))
                s_userFonts[familyName] = tf;

            // Also index by the file name (without extension) so users can
            // drop "Geneva.ttf" and have it match the Mac font name "Geneva".
            var baseName = Path.GetFileNameWithoutExtension(file);
            if (!string.IsNullOrEmpty(baseName) && !s_userFonts.ContainsKey(baseName))
                s_userFonts[baseName] = tf;
        }
    }

    /// <summary>Try to resolve a typeface from the user font directory.</summary>
    private static SKTypeface? TryUserFont(string familyName)
    {
        EnsureUserFontsScanned();
        return s_userFonts.TryGetValue(familyName, out var tf) ? tf : null;
    }

    // ── System-installed font detection (tier 2) ────────────────────────────

    // Cache: null = not checked, non-null = found or checked-and-missing (stored as Default).
    private static readonly Dictionary<string, SKTypeface?> s_systemFontCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Tries to load a font by its real Mac name from the system font collection.
    /// On macOS many classic Mac fonts are system-installed; on Windows/Linux they may
    /// be present if the user installed them manually.
    /// Returns null if the system doesn't have that exact font family.
    /// </summary>
    private static SKTypeface? TrySystemFont(string familyName, SKFontStyleWeight weight, SKFontStyleSlant slant)
    {
        // Quick reject sentinels.
        if (familyName.StartsWith('_')) return null;

        if (s_systemFontCache.TryGetValue(familyName, out var cached))
        {
            if (cached == null) return null; // Previously checked, not available.
            // We have the family — return a styled variant.
            return SKTypeface.FromFamilyName(familyName, weight, SKFontStyleWidth.Normal, slant);
        }

        // Probe the family by asking SkiaSharp.  If the returned typeface's FamilyName
        // doesn't match what we asked for, the system substituted — treat as missing.
        var probe = SKTypeface.FromFamilyName(familyName, SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        if (probe != null && probe.FamilyName.Equals(familyName, StringComparison.OrdinalIgnoreCase))
        {
            s_systemFontCache[familyName] = probe;
            // Return the styled version the caller actually wants.
            if (weight != SKFontStyleWeight.Normal || slant != SKFontStyleSlant.Upright)
                return SKTypeface.FromFamilyName(familyName, weight, SKFontStyleWidth.Normal, slant) ?? probe;
            return probe;
        }

        s_systemFontCache[familyName] = null; // Mark as not available.
        probe?.Dispose();
        return null;
    }

    // ── Classic Mac font ID → name mapping ──────────────────────────────────

    // Maps Mac font IDs to their original Mac font name.
    // This is used for tier 1 + 2 lookups (user dir / system installed).
    private static readonly Dictionary<int, string> MacFontNames = new()
    {
        { 0,  "Chicago" },
        { 1,  "Geneva" },
        { 2,  "New York" },
        { 3,  "Geneva" },
        { 4,  "Monaco" },
        { 5,  "Venice" },
        { 6,  "London" },
        { 7,  "Athens" },
        { 8,  "San Francisco" },
        { 9,  "Toronto" },
        { 11, "Cairo" },
        { 12, "Los Angeles" },
        { 13, "Zapf Dingbats" },
        { 14, "Bookman" },
        { 16, "Palatino" },
        { 20, "Times" },
        { 21, "Helvetica" },
        { 22, "Courier" },
        { 23, "Symbol" },
    };

    // Tier 3+4 fallback: Mac font name → embedded sentinel or common cross-platform font.
    private static readonly Dictionary<string, string> FallbackMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "chicago",    "_chicago" },
        { "charcoal",   "_chicago" },
        { "geneva",     "_geneva" },
        { "helvetica",  "_geneva" },
        { "new york",   "Times New Roman" },
        { "monaco",     "Courier New" },
        { "courier",    "Courier New" },
        { "times",      "Times New Roman" },
        { "palatino",   "Palatino Linotype" },
        { "bookman",    "Georgia" },
        { "symbol",     "Segoe UI Symbol" },
    };

    /// <summary>Returns the best available font family for a Mac font ID.</summary>
    public static string GetFamilyName(int macFontId)
    {
        if (MacFontNames.TryGetValue(macFontId, out var macName))
        {
            // Check user dir and system by original name.
            if (TryUserFont(macName) != null) return macName;
            if (TrySystemFont(macName, SKFontStyleWeight.Normal, SKFontStyleSlant.Upright) != null) return macName;
            // Fall back to substitute.
            if (FallbackMap.TryGetValue(macName, out var sub)) return sub;
        }
        return "Arial";
    }

    /// <summary>
    /// Returns the font family name for a Mac font ID, preferring the FTBL font-name string.
    /// </summary>
    public static string GetFamilyName(int macFontId, FontTableBlock? fontTable)
    {
        // The FTBL stores the actual font name used when the stack was created.
        if (fontTable != null)
        {
            var name = fontTable.GetFontName(macFontId);
            if (!string.IsNullOrWhiteSpace(name))
                return ResolveFontName(name);
        }
        return GetFamilyName(macFontId);
    }

    /// <summary>
    /// Resolves a Mac font name through the full priority chain:
    /// user dir → system installed → embedded substitute → original name (let SkiaSharp try).
    /// </summary>
    private static string ResolveFontName(string macName)
    {
        // Tier 1: user font directory.
        if (TryUserFont(macName) != null) return macName;

        // Tier 2: system-installed font with exact name.
        if (TrySystemFont(macName, SKFontStyleWeight.Normal, SKFontStyleSlant.Upright) != null) return macName;

        // Tier 3+4: embedded substitute or common cross-platform font.
        if (FallbackMap.TryGetValue(macName, out var sub)) return sub;

        // Unknown font — return the original name and let SkiaSharp try.
        return macName;
    }

    /// <summary>Creates an SKTypeface for the given Mac font ID and style byte.</summary>
    public static SKTypeface GetTypeface(int macFontId, byte textStyle)
        => GetTypeface(macFontId, textStyle, null);

    /// <summary>
    /// Creates an SKTypeface for the given Mac font ID, style byte, and optional FTBL.
    /// </summary>
    public static SKTypeface GetTypeface(int macFontId, byte textStyle, FontTableBlock? fontTable)
    {
        bool isBold   = (textStyle & 0x01) != 0;
        bool isItalic = (textStyle & 0x02) != 0;
        var weight = isBold   ? SKFontStyleWeight.Bold   : SKFontStyleWeight.Normal;
        var slant  = isItalic ? SKFontStyleSlant.Italic  : SKFontStyleSlant.Upright;

        // Determine the raw Mac font name from FTBL or ID table.
        string? macName = null;
        if (fontTable != null)
        {
            var ftblName = fontTable.GetFontName(macFontId);
            if (!string.IsNullOrWhiteSpace(ftblName))
                macName = ftblName;
        }
        macName ??= MacFontNames.GetValueOrDefault(macFontId);

        // Tier 1: User font directory — try the original Mac name.
        if (macName != null)
        {
            var userTf = TryUserFont(macName);
            if (userTf != null) return userTf;
        }

        // Tier 2: System-installed font with exact Mac name.
        if (macName != null)
        {
            var sysTf = TrySystemFont(macName, weight, slant);
            if (sysTf != null) return sysTf;
        }

        // Tier 3: Embedded substitutes (ChicagoFLF, Noto Sans).
        var family = macName != null && FallbackMap.TryGetValue(macName, out var mapped) ? mapped : null;
        family ??= GetFamilyName(macFontId, fontTable);

        if (family == "_chicago")
            return s_chicagoFlf.Value ?? SKTypeface.Default;

        if (family == "_geneva")
            return (isBold ? s_notoBold.Value : s_notoRegular.Value) ?? SKTypeface.Default;

        // Tier 4: Common cross-platform font.
        return SKTypeface.FromFamilyName(family, weight, SKFontStyleWidth.Normal, slant)
               ?? SKTypeface.Default;
    }

    /// <summary>
    /// Resolves an SKTypeface for a styled run, falling back to the Part's defaults
    /// when the StyleEntry uses inherit (-1) values.
    /// </summary>
    public static SKTypeface GetTypefaceForRun(
        StyleEntry entry,
        Part fallbackPart,
        FontTableBlock? fontTable)
    {
        int fontId    = entry.InheritFont  ? fallbackPart.TextFontId : entry.TextFontId;
        byte styleByte = entry.InheritStyle ? fallbackPart.TextStyle  : (byte)(entry.TextStyle < 0 ? 0 : entry.TextStyle);
        return GetTypeface(fontId, styleByte, fontTable);
    }

    /// <summary>
    /// Returns the resolved point size for a styled run.
    /// </summary>
    public static float GetSizeForRun(StyleEntry entry, Part fallbackPart)
    {
        if (!entry.InheritSize && entry.TextSize > 0) return entry.TextSize;
        return fallbackPart.TextSize > 0 ? fallbackPart.TextSize : 12f;
    }

    /// <summary>Returns the resolved style-flag byte for a styled run.</summary>
    public static byte GetStyleFlagsForRun(StyleEntry entry, Part fallbackPart)
        => entry.InheritStyle ? fallbackPart.TextStyle : (byte)(entry.TextStyle < 0 ? 0 : entry.TextStyle);
}
