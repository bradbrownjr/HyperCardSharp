using System.Reflection;
using HyperCardSharp.Core.Parts;
using HyperCardSharp.Core.Stack;
using SkiaSharp;

namespace HyperCardSharp.Rendering;

/// <summary>
/// Maps classic Macintosh font IDs to SkiaSharp typefaces.
/// HyperCard used Mac system font IDs; we substitute with available cross-platform fonts.
/// Chicago (font 0) is substituted with ChicagoFLF — a free Chicago lookalike (MIT licence,
/// bundled from pfcode/system7css https://github.com/pfcode/system7css).
/// </summary>
public static class FontMapper
{
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
    // Geneva was the primary UI font in System 7; we use Noto Sans as a visually
    // faithful cross-platform stand-in for clean small-size Latin text.
    private static readonly Lazy<SKTypeface?> s_notoRegular = new(() => LoadEmbeddedTypeface("NotoSans-Regular.ttf"));
    private static readonly Lazy<SKTypeface?> s_notoBold    = new(() => LoadEmbeddedTypeface("NotoSans-Bold.ttf"));

    private static SKTypeface? LoadEmbeddedTypeface(string fileName)
    {
        var asm = typeof(FontMapper).Assembly;
        using var stream = asm.GetManifestResourceStream(
            $"HyperCardSharp.Rendering.Assets.{fileName}");
        return stream != null ? SKTypeface.FromStream(stream) : null;
    }
    // Classic Mac font IDs → cross-platform font family names.
    // Font 0 (System/Chicago) uses the embedded ChicagoFLF (MIT licence).
    // Fonts 1 and 3 (Application font / Geneva) use the embedded Noto Sans (SIL OFL).
    // Primary source: Inside Macintosh, Volume I, Font Manager chapter.
    private static readonly Dictionary<int, string> FontFamilyMap = new()
    {
        { 0,  "_chicago" },           // System (Chicago) → ChicagoFLF (embedded)
        { 1,  "_geneva" },            // Application font (Geneva in System 7) → Noto Sans (embedded)
        { 2,  "Times New Roman" },    // New York → Times
        { 3,  "_geneva" },            // Geneva → Noto Sans (embedded)
        { 4,  "Courier New" },        // Monaco → Courier New (monospace)
        { 5,  "_geneva" },            // Cairo (pictographic, fall back to Noto)
        { 6,  "_geneva" },            // Los Angeles
        { 13, "_geneva" },            // Zapf Dingbats (fall back to Noto)
        { 14, "Georgia" },            // Bookman → Georgia
        { 16, "Palatino Linotype" },  // Palatino
        { 20, "Times New Roman" },    // Times
        { 21, "_geneva" },            // Helvetica → Noto Sans (embedded)
        { 22, "Courier New" },        // Courier
        { 23, "_geneva" },            // Symbol (fall back to Noto)
    };

    /// <summary>Returns the best available cross-platform font family for a Mac font ID.</summary>
    public static string GetFamilyName(int macFontId)
        => FontFamilyMap.TryGetValue(macFontId, out var name) ? name : "Arial";

    /// <summary>
    /// Returns the font family name for a Mac font ID, preferring the FTBL font-name string
    /// when available (stacks embed the original font name directly).
    /// </summary>
    public static string GetFamilyName(int macFontId, FontTableBlock? fontTable)
    {
        // The FTBL stores the actual font name used when the stack was created.
        // Try to map well-known classic Mac names to modern equivalents.
        if (fontTable != null)
        {
            var name = fontTable.GetFontName(macFontId);
            if (!string.IsNullOrWhiteSpace(name))
                return MapMacFontName(name);
        }
        return GetFamilyName(macFontId);
    }

    /// <summary>Maps a classic Mac font name to the best available modern substitute.</summary>
    private static string MapMacFontName(string macName) =>
        macName.ToLowerInvariant() switch
        {
            "chicago"   => "_chicago",    // embedded ChicagoFLF
            "charcoal"  => "_chicago",    // Charcoal is Chicago's System 8 successor
            "geneva"    => "_geneva",     // embedded Noto Sans Regular/Bold (SIL OFL)
            "helvetica" => "_geneva",     // Helvetica → same substitute as Geneva
            "new york"  => "Times New Roman",
            "monaco"    => "Courier New",
            "courier"   => "Courier New",
            "times"     => "Times New Roman",
            "palatino"  => "Palatino Linotype",
            "bookman"   => "Georgia",
            _           => macName, // Try the original name; SkiaSharp will fall back to default if unavailable
        };

    /// <summary>Creates an SKTypeface for the given Mac font ID and style byte.</summary>
    public static SKTypeface GetTypeface(int macFontId, byte textStyle)
        => GetTypeface(macFontId, textStyle, null);

    /// <summary>
    /// Creates an SKTypeface for the given Mac font ID, style byte, and optional FTBL.
    /// </summary>
    public static SKTypeface GetTypeface(int macFontId, byte textStyle, FontTableBlock? fontTable)
    {
        var family = GetFamilyName(macFontId, fontTable);
        bool isBold   = (textStyle & 0x01) != 0;
        bool isItalic = (textStyle & 0x02) != 0;

        // Chicago (sentinel "_chicago") uses the embedded ChicagoFLF typeface directly.
        if (family == "_chicago")
            return s_chicagoFlf.Value ?? SKTypeface.Default;

        // Geneva (sentinel "_geneva") uses embedded Noto Sans Regular or Bold.
        if (family == "_geneva")
            return (isBold ? s_notoBold.Value : s_notoRegular.Value) ?? SKTypeface.Default;

        var weight = isBold   ? SKFontStyleWeight.Bold   : SKFontStyleWeight.Normal;
        var slant  = isItalic ? SKFontStyleSlant.Italic  : SKFontStyleSlant.Upright;

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
