using HyperCardSharp.Core.Parts;
using HyperCardSharp.Core.Stack;
using SkiaSharp;

namespace HyperCardSharp.Rendering;

/// <summary>
/// Overlays part visuals (field text, button chrome) onto a rendered card.
///
/// Architecture note: HyperCard stores button and field border graphics baked into
/// the WOBA bitmap. The bitmap already includes the visual appearance of most parts.
/// PartRenderer's primary job is therefore to render field TEXT CONTENT, which is
/// stored separately in PartContent records and is not in the WOBA bitmap.
///
/// Button outlines are drawn only for styles that are not painted into the WOBA
/// (Transparent and Opaque), so they appear as visible interactive regions.
/// </summary>
public static class PartRenderer
{
    /// <summary>
    /// Renders background-field text for the current card.
    /// Background part content that differs per card lives in card.PartContents (negative IDs);
    /// shared background text lives in bg.PartContents (positive IDs).
    /// </summary>
    public static void RenderBackgroundParts(
        SKCanvas canvas,
        BackgroundBlock bg,
        CardBlock card)
    {
        // Build a lookup of card-specific content for background parts.
        // Per the HC format: background part IDs in card.PartContents are stored as negative values.
        var cardBgContent = card.PartContents
            .Where(pc => pc.PartId < 0)
            .ToDictionary(pc => (short)(-pc.PartId), pc => pc.Text);

        // Shared background content (same on every card that uses this background)
        var sharedBgContent = bg.PartContents
            .ToDictionary(pc => pc.PartId, pc => pc.Text);

        foreach (var part in bg.Parts)
        {
            if (!part.Visible || !part.IsField) continue;

            // Card-specific content takes priority over shared background content.
            string text = cardBgContent.TryGetValue(part.PartId, out var cardText)
                ? cardText
                : sharedBgContent.TryGetValue(part.PartId, out var sharedText)
                    ? sharedText
                    : "";

            if (!string.IsNullOrEmpty(text))
                TextRenderer.DrawFieldText(canvas, part, text);
        }
    }

    /// <summary>
    /// Renders card-local part content (field text for parts on this specific card).
    /// </summary>
    public static void RenderCardParts(SKCanvas canvas, CardBlock card)
    {
        // Card part content entries have positive IDs matching card.Parts
        var contentLookup = card.PartContents
            .Where(pc => pc.PartId > 0)
            .ToDictionary(pc => pc.PartId, pc => pc.Text);

        foreach (var part in card.Parts)
        {
            if (!part.Visible || !part.IsField) continue;

            if (contentLookup.TryGetValue(part.PartId, out var text) && !string.IsNullOrEmpty(text))
                TextRenderer.DrawFieldText(canvas, part, text);
        }
    }

    /// <summary>
    /// Draws a visible outline for Transparent or Opaque buttons that have no WOBA visual.
    /// Rectangle buttons are already in the WOBA bitmap; skip those to avoid double-rendering.
    /// </summary>
    public static void RenderInvisibleButtons(SKCanvas canvas, IEnumerable<Part> parts)
    {
        using var borderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(0, 0, 0, 64), // semi-transparent, not to clash with WOBA
            StrokeWidth = 1,
        };

        foreach (var part in parts)
        {
            if (!part.Visible || !part.IsButton) continue;
            if (part.Style != PartStyle.Transparent && part.Style != PartStyle.Opaque) continue;
            if (string.IsNullOrWhiteSpace(part.Script)) continue;  // invisible hotspot with no script

            var rect = new SKRect(part.Left, part.Top, part.Right, part.Bottom);
            canvas.DrawRect(rect, borderPaint);
        }
    }
}
