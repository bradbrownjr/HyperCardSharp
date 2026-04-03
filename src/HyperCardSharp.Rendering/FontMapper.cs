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
    // Classic Mac font IDs → cross-platform font family names.
    // Font 0 (System/Chicago) uses the embedded ChicagoFLF (MIT licence).
    // Font 1 (Application font = Geneva in System 7) uses Arial — Chicago is WRONG here.
    // Primary source: Inside Macintosh, Volume I, Font Manager chapter.
    private static readonly Dictionary<int, string> FontFamilyMap = new()
    {
        { 0,  "_chicago" },           // System (Chicago) → ChicagoFLF (embedded)
        { 1,  "Arial" },              // Application font (Geneva in System 7) → Arial
        { 2,  "Times New Roman" },    // New York → Times
        { 3,  "Arial" },              // Geneva → Arial
        { 4,  "Courier New" },        // Monaco → Courier New (monospace)
        { 5,  "Arial" },              // Cairo (pictographic, fall back)
        { 6,  "Arial" },              // Los Angeles
        { 13, "Arial" },              // Zapf Dingbats (fall back)
        { 14, "Georgia" },            // Bookman → Georgia
        { 16, "Palatino Linotype" },  // Palatino
        { 20, "Times New Roman" },    // Times
        { 21, "Arial" },              // Helvetica → Arial
        { 22, "Courier New" },        // Courier
        { 23, "Arial" },              // Symbol (fall back)
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
            "geneva"    => "Arial",
            "new york"  => "Times New Roman",
            "monaco"    => "Courier New",
            "courier"   => "Courier New",
            "times"     => "Times New Roman",
            "helvetica" => "Arial",
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

        // Chicago (sentinel "_chicago") uses the embedded ChicagoFLF typeface directly.
        // ChicagoFLF has no true bold/italic variants; bold style is honoured by SkiaSharp
        // synthesizing faux-bold, and italic falls back gracefully.
        if (family == "_chicago")
            return s_chicagoFlf.Value ?? SKTypeface.Default;

        bool bold   = (textStyle & 0x01) != 0;
        bool italic = (textStyle & 0x02) != 0;

        var weight = bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
        var slant  = italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;

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
