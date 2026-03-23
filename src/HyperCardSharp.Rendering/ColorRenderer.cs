using HyperCardSharp.Core.Resources;
using SkiaSharp;

namespace HyperCardSharp.Rendering;

/// <summary>
/// Applies AddColor (HCcd/HCbg) color overlays to a rendered B&amp;W card bitmap.
///
/// AddColor was a HyperCard 2.x extension XCMD that stored per-card color region
/// data in the resource fork. This renderer composites those regions on top of
/// the B&amp;W WOBA bitmap to produce color output.
///
/// TODO: full implementation pending AddColorDecoder and resource fork extraction.
/// Currently returns the source bitmap unchanged if no color data is provided.
/// </summary>
public static class ColorRenderer
{
    /// <summary>
    /// Composites color regions from AddColor data onto the base B&W bitmap.
    /// If colorRegions is empty, returns baseBitmap unchanged (no allocation).
    /// </summary>
    public static SKBitmap ApplyColorOverlays(
        SKBitmap baseBitmap,
        IReadOnlyList<AddColorDecoder.ColorRegion> colorRegions)
    {
        if (colorRegions.Count == 0)
            return baseBitmap;

        // TODO: implement color region compositing when AddColorDecoder is complete.
        // For now, degrade gracefully by returning the B&W bitmap.
        return baseBitmap;
    }
}
