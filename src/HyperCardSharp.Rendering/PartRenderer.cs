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
    /// Renders background-field text and non-Transparent button chrome for the current card.
    /// Background part content that differs per card lives in card.PartContents (negative IDs);
    /// shared background text lives in bg.PartContents (positive IDs).
    /// </summary>
    public static void RenderBackgroundParts(
        SKCanvas canvas,
        BackgroundBlock bg,
        CardBlock card,
        (int Left, int Top, int Right, int Bottom)? wobaImgRect = null)
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
            if (!part.Visible) continue;

            if (part.IsField)
            {
                // Card-specific content takes priority over shared background content.
                string text = cardBgContent.TryGetValue(part.PartId, out var cardText)
                    ? cardText
                    : sharedBgContent.TryGetValue(part.PartId, out var sharedText)
                        ? sharedText
                        : "";

                if (!string.IsNullOrEmpty(text))
                    TextRenderer.DrawFieldText(canvas, part, text);
            }
            else if (part.IsButton)
            {
                RenderButtonChrome(canvas, part, wobaImgRect);
            }
        }
    }

    /// <summary>
    /// Renders card-local part content (field text) and non-Transparent button chrome.
    /// Button chrome is always drawn to guarantee visibility even for buttons that are
    /// outside the card WOBA's imgRect (i.e. never baked into the bitmap).
    /// </summary>
    /// <param name="wobaImgRect">The WOBA image rect for this card, or null if unknown.
    /// Buttons that fall fully inside the WOBA imgRect already have their outline baked
    /// in; we skip re-rendering their name text to avoid double-drawing.</param>
    public static void RenderCardParts(SKCanvas canvas, CardBlock card,
        (int Left, int Top, int Right, int Bottom)? wobaImgRect = null)
    {
        // Card part content entries have positive IDs matching card.Parts
        var contentLookup = card.PartContents
            .Where(pc => pc.PartId > 0)
            .ToDictionary(pc => pc.PartId, pc => pc.Text);

        foreach (var part in card.Parts)
        {
            if (!part.Visible) continue;

            if (part.IsField)
            {
                if (contentLookup.TryGetValue(part.PartId, out var text) && !string.IsNullOrEmpty(text))
                    TextRenderer.DrawFieldText(canvas, part, text);
            }
            else if (part.IsButton)
            {
                RenderButtonChrome(canvas, part, wobaImgRect);
            }
        }
    }

    // ── Button chrome rendering ───────────────────────────────────────────────

    private static readonly SKColor ButtonBorderColor = SKColors.Black;
    private const float ButtonCornerRadius = 8f;

    /// <summary>
    /// Draws the outline and optional name label of a non-Transparent button.
    /// Uses stroke-only rendering so it overlays cleanly on top of the WOBA bitmap
    /// without erasing any baked-in pixels.
    /// </summary>
    /// <param name="wobaImgRect">When provided, name text is only rendered for
    /// buttons that fall (even partially) outside the WOBA image rect — those
    /// buttons were never baked into the WOBA so their label would otherwise be
    /// invisible.</param>
    private static void RenderButtonChrome(
        SKCanvas canvas, Part part,
        (int Left, int Top, int Right, int Bottom)? wobaImgRect)
    {
        if (part.Style == PartStyle.Transparent) return;  // intentionally invisible
        if (part.Width <= 0 || part.Height <= 0) return;

        var rect = new SKRect(part.Left, part.Top, part.Right, part.Bottom);

        using var borderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = ButtonBorderColor,
            StrokeWidth = 1,
            IsAntialias = false
        };

        if (part.Style == PartStyle.Opaque)
        {
            // Opaque: white fill, no border
            using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = SKColors.White };
            canvas.DrawRect(rect, fillPaint);
            return;
        }

        // Draw outline based on style
        switch (part.Style)
        {
            case PartStyle.RoundRect:
            case PartStyle.Standard:
            case PartStyle.Default:
            case PartStyle.Oval:
                canvas.DrawRoundRect(rect, ButtonCornerRadius, ButtonCornerRadius, borderPaint);
                break;
            case PartStyle.Shadow:
                canvas.DrawRect(rect, borderPaint);
                using (var shadowPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = ButtonBorderColor })
                {
                    // Simple 3px drop-shadow on right and bottom
                    canvas.DrawRect(new SKRect(rect.Left + 3, rect.Bottom, rect.Right + 3, rect.Bottom + 3), shadowPaint);
                    canvas.DrawRect(new SKRect(rect.Right, rect.Top + 3, rect.Right + 3, rect.Bottom + 3), shadowPaint);
                }
                break;
            default:  // Rectangle, CheckBox, RadioButton, etc.
                canvas.DrawRect(rect, borderPaint);
                break;
        }

        // Draw button name if the button's label is not already baked into the WOBA.
        // A button's label is considered "in the WOBA" when its full rect is contained
        // within the WOBA image rect.  If any part falls outside, we render the label
        // ourselves to ensure visibility.
        if (!part.ShowName || string.IsNullOrEmpty(part.Name)) return;

        bool outsideWoba = wobaImgRect is null ||
            part.Left  < wobaImgRect.Value.Left  ||
            part.Top   < wobaImgRect.Value.Top   ||
            part.Right > wobaImgRect.Value.Right  ||
            part.Bottom > wobaImgRect.Value.Bottom;

        if (!outsideWoba) return;  // label already baked in WOBA

        // Draw the label centred in the button rect (white backdrop first so it's legible)
        float cx = rect.MidX;
        float cy = rect.MidY;
        float textSize = part.TextSize > 0 ? part.TextSize : 12f;

        using var typeface = FontMapper.GetTypeface(part.TextFontId, part.TextStyle);
        using var labelFont = new SKFont(typeface, textSize);
        using var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = false };

        float tw = labelFont.MeasureText(part.Name);
        float th = textSize;
        float tx = cx - tw / 2f;
        float ty = cy + th / 2f - 1f;

        // White backdrop behind text so it pops against any WOBA content
        using var backdropPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = SKColors.White };
        canvas.DrawRect(new SKRect(tx - 1, ty - th, tx + tw + 1, ty + 2), backdropPaint);
        canvas.DrawText(part.Name, tx, ty, labelFont, textPaint);
    }

    // Kept for backwards compatibility with any callers.
    [System.Obsolete("Use RenderCardParts / RenderBackgroundParts — button chrome is now included there.")]
    public static void RenderInvisibleButtons(SKCanvas canvas, IEnumerable<Part> parts) { }
}
