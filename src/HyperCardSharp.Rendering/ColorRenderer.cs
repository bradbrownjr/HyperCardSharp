using HyperCardSharp.Core.Resources;
using SkiaSharp;

namespace HyperCardSharp.Rendering;

/// <summary>
/// Applies AddColor (HCcd/HCbg) colour overlays onto an SKCanvas.
///
/// AddColor regions are drawn BEFORE the 1-bit bitmaps so that white areas
/// of the B&amp;W artwork let the colour fill show through (using the transparent
/// bitmap variant from <see cref="BitmapRenderer.ToSKBitmapWithTransparency"/>).
/// </summary>
public static class ColorRenderer
{
    /// <summary>
    /// Draw AddColor fill regions onto the canvas.
    /// Call this before drawing the corresponding BMAP so that the colour shows
    /// through white areas of the 1-bit artwork.
    /// </summary>
    /// <param name="canvas">Target canvas.</param>
    /// <param name="regions">Color regions decoded from an HCcd or HCbg resource.</param>
    /// <param name="parts">
    /// Optional part list — used to look up the rect for a region that targets a part
    /// (region.PartId > 0).  Pass null to skip part-targeted regions.
    /// </param>
    public static void DrawColorRegions(
        SKCanvas canvas,
        IReadOnlyList<AddColorDecoder.ColorRegion> regions,
        IReadOnlyList<HyperCardSharp.Core.Parts.Part>? parts = null)
    {
        using var fillPaint = new SKPaint
        {
            Style       = SKPaintStyle.Fill,
            IsAntialias = false,
        };

        foreach (var r in regions)
        {
            SKRect rect;

            if (r.PartId == 0 || r.PartId < 0)
            {
                // Region targets the card/background background area.
                rect = new SKRect(r.Left, r.Top, r.Right, r.Bottom);
            }
            else if (parts != null)
            {
                // Region targets a specific part — look it up.
                var part = parts.FirstOrDefault(p => p.PartId == r.PartId);
                if (part == null) continue;
                rect = (r.Left != 0 || r.Right != 0)
                    ? new SKRect(r.Left, r.Top, r.Right, r.Bottom)
                    : new SKRect(part.Left, part.Top, part.Right, part.Bottom);
            }
            else
            {
                continue; // Part-targeted region but no part list provided
            }

            if (rect.IsEmpty) continue;

            fillPaint.Color = new SKColor(
                (byte)((r.FillColor >> 16) & 0xFF),   // R
                (byte)((r.FillColor >>  8) & 0xFF),   // G
                (byte)( r.FillColor        & 0xFF),   // B
                0xFF);

            canvas.DrawRect(rect, fillPaint);
        }
    }

    /// <summary>
    /// Composites color regions from AddColor data onto the base B&amp;W bitmap.
    /// This fallback creates a new bitmap; the preferred path is to use
    /// <see cref="DrawColorRegions"/> directly on the canvas before drawing bitmaps.
    /// If colorRegions is empty, returns baseBitmap unchanged (no allocation).
    /// </summary>
    public static SKBitmap ApplyColorOverlays(
        SKBitmap baseBitmap,
        IReadOnlyList<AddColorDecoder.ColorRegion> colorRegions)
    {
        if (colorRegions.Count == 0)
            return baseBitmap;

        var output = baseBitmap.Copy();
        using var canvas = new SKCanvas(output);
        DrawColorRegions(canvas, colorRegions);
        return output;
    }
}

