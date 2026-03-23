using SkiaSharp;

namespace HyperCardSharp.Rendering;

/// <summary>
/// Maps classic Macintosh font IDs to SkiaSharp typefaces.
/// HyperCard used Mac system font IDs; we substitute with available cross-platform fonts.
/// </summary>
public static class FontMapper
{
    // Classic Mac font IDs → cross-platform font family names.
    // Primary source: Inside Macintosh, Volume I, Font Manager chapter.
    private static readonly Dictionary<int, string> FontFamilyMap = new()
    {
        { 0,  "Arial" },           // System (Chicago on Mac)
        { 1,  "Arial" },           // Application font (Geneva equivalent)
        { 3,  "Arial" },           // Geneva → Arial (closest sans-serif)
        { 4,  "Courier New" },     // Monaco → Courier New (monospace)
        { 5,  "Arial" },           // Cairo (pictographic, fall back)
        { 6,  "Arial" },           // Los Angeles
        { 13, "Arial" },           // Zapf Dingbats (fall back)
        { 14, "Georgia" },         // Bookman → Georgia
        { 16, "Palatino Linotype" }, // Palatino
        { 20, "Times New Roman" }, // Times
        { 21, "Arial" },           // Helvetica → Arial
        { 22, "Courier New" },     // Courier
        { 23, "Arial" },           // Symbol (fall back)
    };

    /// <summary>Returns the best available cross-platform font family for a Mac font ID.</summary>
    public static string GetFamilyName(int macFontId)
        => FontFamilyMap.TryGetValue(macFontId, out var name) ? name : "Arial";

    /// <summary>Creates an SKTypeface for the given Mac font ID and style byte.</summary>
    public static SKTypeface GetTypeface(int macFontId, byte textStyle)
    {
        var family = GetFamilyName(macFontId);
        bool bold   = (textStyle & 0x01) != 0;
        bool italic = (textStyle & 0x02) != 0;

        var weight = bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
        var slant  = italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;

        return SKTypeface.FromFamilyName(family, weight, SKFontStyleWidth.Normal, slant)
               ?? SKTypeface.Default;
    }
}
