using HyperCardSharp.Core.Parts;
using HyperCardSharp.Core.Stack;
using SkiaSharp;

namespace HyperCardSharp.Rendering;

/// <summary>
/// Overlays part visuals (field text, button chrome) onto a rendered card.
///
/// Rendering model:
/// 1. The WOBA bitmap is drawn first — this is purely the painted artwork layer.
/// 2. PartRenderer then draws all visible parts ON TOP of the WOBA, exactly as
///    HyperCard's runtime does.  Button chrome (white fill, border, icon, label)
///    is always rendered dynamically; it is never baked into the WOBA.
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
        IReadOnlyDictionary<short, SKBitmap>? icons = null,
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

                if (part.Style == PartStyle.Scrolling)
                    RenderScrollbarChrome(canvas, part);
            }
            else if (part.IsButton)
            {
                RenderButtonChrome(canvas, part, icons, wobaImgRect);
            }
        }
    }

    /// <summary>
    /// Renders card-local part content (field text) and non-Transparent button chrome.
    /// Button chrome is always drawn since HyperCard renders it dynamically over the WOBA.
    /// </summary>
    public static void RenderCardParts(SKCanvas canvas, CardBlock card,
        IReadOnlyDictionary<short, SKBitmap>? icons = null,
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

                if (part.Style == PartStyle.Scrolling)
                    RenderScrollbarChrome(canvas, part);
            }
            else if (part.IsButton)
            {
                RenderButtonChrome(canvas, part, icons, wobaImgRect);
            }
        }
    }

    // ── Button chrome rendering ───────────────────────────────────────────────

    private static readonly SKColor ButtonBorderColor = SKColors.Black;
    private const float ButtonCornerRadius = 8f;
    private const float ScrollbarWidth = 15f; // Classic Mac scrollbar width

    /// <summary>
    /// Draws a classic System 7 scrollbar on the right edge of a scrolling field.
    /// This is static (non-interactive) chrome — the thumb is rendered at the top to
    /// indicate "scrolled to start" from the viewer's always-from-top rendering.
    /// </summary>
    private static void RenderScrollbarChrome(SKCanvas canvas, Part part)
    {
        if (part.Width <= ScrollbarWidth || part.Height <= 0) return;

        float left   = part.Right - ScrollbarWidth;
        float top    = part.Top;
        float right  = part.Right;
        float bottom = part.Bottom;

        using var whiteFill  = new SKPaint { Style = SKPaintStyle.Fill,   Color = SKColors.White };
        using var blackBorder = new SKPaint { Style = SKPaintStyle.Stroke, Color = SKColors.Black, StrokeWidth = 1 };
        using var blackFill   = new SKPaint { Style = SKPaintStyle.Fill,   Color = SKColors.Black };

        // Scrollbar track background
        var trackRect = new SKRect(left, top, right, bottom);
        canvas.DrawRect(trackRect, whiteFill);
        canvas.DrawRect(trackRect, blackBorder);

        const float arrowH = 15f;

        // Up arrow box at the top
        var upBox = new SKRect(left, top, right, top + arrowH);
        canvas.DrawRect(upBox, whiteFill);
        canvas.DrawRect(upBox, blackBorder);
        // Up triangle
        float ax = left + ScrollbarWidth / 2f;
        float ay = top + arrowH * 0.65f;
        float aw = 4f;
        using var triPath = new SKPath();
        triPath.MoveTo(ax,      ay - aw);
        triPath.LineTo(ax - aw, ay + aw * 0.5f);
        triPath.LineTo(ax + aw, ay + aw * 0.5f);
        triPath.Close();
        canvas.DrawPath(triPath, blackFill);

        // Down arrow box at the bottom
        var downBox = new SKRect(left, bottom - arrowH, right, bottom);
        canvas.DrawRect(downBox, whiteFill);
        canvas.DrawRect(downBox, blackBorder);
        // Down triangle
        float bx = left + ScrollbarWidth / 2f;
        float by = bottom - arrowH * 0.65f;
        using var triPath2 = new SKPath();
        triPath2.MoveTo(bx,      by + aw);
        triPath2.LineTo(bx - aw, by - aw * 0.5f);
        triPath2.LineTo(bx + aw, by - aw * 0.5f);
        triPath2.Close();
        canvas.DrawPath(triPath2, blackFill);

        // Thumb — small rectangle near the top (represents scrolled-to-top)
        float thumbAreaTop = top + arrowH + 1;
        float thumbAreaH   = bottom - arrowH - arrowH - 2;
        if (thumbAreaH > 6)
        {
            float thumbH = Math.Min(thumbAreaH * 0.2f, 20f);
            thumbH = Math.Max(thumbH, 8f);
            var thumbRect = new SKRect(left + 2, thumbAreaTop, right - 2, thumbAreaTop + thumbH);
            canvas.DrawRect(thumbRect, blackFill);
        }
    }

    /// <summary>
    /// Draws full HyperCard button chrome: white fill, outline, icon (if any), and label.
    ///
    /// HyperCard always renders button chrome OVER the WOBA bitmap at runtime —
    /// the WOBA only stores the painted artwork, never button visuals.  So we
    /// always draw the full chrome regardless of the WOBA imgRect.
    /// </summary>
    private static void RenderButtonChrome(SKCanvas canvas, Part part,
        IReadOnlyDictionary<short, SKBitmap>? icons,
        (int Left, int Top, int Right, int Bottom)? wobaImgRect)
    {
        if (part.Style == PartStyle.Transparent) return;  // click zone only, no visual
        if (part.Width <= 0 || part.Height <= 0) return;

        var rect = new SKRect(part.Left, part.Top, part.Right, part.Bottom);

        using var fillPaint  = new SKPaint { Style = SKPaintStyle.Fill,   Color = SKColors.White };
        using var borderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = ButtonBorderColor,
            StrokeWidth = 1,
            IsAntialias = false
        };

        switch (part.Style)
        {
            case PartStyle.Opaque:
                // White fill, no border
                canvas.DrawRect(rect, fillPaint);
                return;

            case PartStyle.RoundRect:
            case PartStyle.Standard:
            case PartStyle.Default:
                // Classic Mac RoundRect buttons always have a 3-px drop shadow
                DrawRoundRectShadow(canvas, rect, ButtonCornerRadius);
                canvas.DrawRoundRect(rect, ButtonCornerRadius, ButtonCornerRadius, fillPaint);
                canvas.DrawRoundRect(rect, ButtonCornerRadius, ButtonCornerRadius, borderPaint);
                // Default style gets an extra thick border (double border)
                if (part.Style == PartStyle.Default)
                {
                    var innerRect = new SKRect(rect.Left + 3, rect.Top + 3, rect.Right - 3, rect.Bottom - 3);
                    canvas.DrawRoundRect(innerRect, ButtonCornerRadius - 2, ButtonCornerRadius - 2, borderPaint);
                }
                break;

            case PartStyle.Oval:
                canvas.DrawOval(rect, fillPaint);
                canvas.DrawOval(rect, borderPaint);
                break;

            case PartStyle.Shadow:
                canvas.DrawRect(rect, fillPaint);
                canvas.DrawRect(rect, borderPaint);
                using (var shadowPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = ButtonBorderColor })
                {
                    canvas.DrawRect(new SKRect(rect.Left + 3, rect.Bottom,     rect.Right + 3, rect.Bottom + 3), shadowPaint);
                    canvas.DrawRect(new SKRect(rect.Right,    rect.Top   + 3,  rect.Right + 3, rect.Bottom + 3), shadowPaint);
                }
                break;

            default:  // Rectangle, CheckBox, RadioButton
                canvas.DrawRect(rect, fillPaint);
                canvas.DrawRect(rect, borderPaint);
                break;
        }

        // ─ Icon ────────────────────────────────────────────────────────────────
        bool hasIcon = part.IconId != 0;
        float textSize = part.TextSize > 0 ? part.TextSize : 12f;

        // Area available for text
        float textAreaTop = rect.Top;

        if (hasIcon)
        {
            // HyperCard renders ICON resources at their native 32×32 size — never upscaled.
            // We only scale DOWN when the button is smaller than 32px in either dimension.
            const float iconPad = 2f;
            float iconAreaH = part.ShowName && !string.IsNullOrEmpty(part.Name)
                ? rect.Height * 0.65f
                : rect.Height - iconPad * 2;
            float iconSize = Math.Min(Math.Min(rect.Width - iconPad * 2, iconAreaH), 32f);
            float iconX = rect.MidX - iconSize / 2f;
            float iconY = rect.Top + iconPad;

            if (icons != null && icons.TryGetValue(part.IconId, out var iconBitmap))
            {
                var destRect = new SKRect(iconX, iconY, iconX + iconSize, iconY + iconSize);
                // SkiaSharp 3.x: use SKImage + DrawImage for nearest-neighbor sampling on a rect dest
                using var iconImage = SKImage.FromBitmap(iconBitmap);
                canvas.DrawImage(iconImage, destRect,
                    new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
            }
            else
            {
                DrawPlaceholderIcon(canvas, iconX, iconY, iconSize);
            }

            textAreaTop = iconY + iconSize;
        }

        // ─ Label ────────────────────────────────────────────────────────────────
        if (!part.ShowName || string.IsNullOrEmpty(part.Name)) return;

        using var typeface  = FontMapper.GetTypeface(part.TextFontId, part.TextStyle);

        // Auto-shrink font size if label is wider than the button (minimum 9pt).
        // 6px total horizontal margin (3px each side) matches HyperCard's Chicago font metrics.
        float labelMaxWidth = rect.Width - 6f;
        float effectiveSize = textSize;
        while (effectiveSize > 9f)
        {
            using var probe = new SKFont(typeface, effectiveSize);
            if (probe.MeasureText(part.Name) <= labelMaxWidth)
                break;
            effectiveSize -= 1f;
        }

        using var labelFont = new SKFont(typeface, effectiveSize);
        using var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = false };

        float tw = labelFont.MeasureText(part.Name);
        float tx = rect.MidX - tw / 2f;

        // Vertical position: centre in remaining text area
        float remainH = rect.Bottom - textAreaTop;
        float ty = textAreaTop + remainH / 2f + effectiveSize / 2f - 1f;

        canvas.DrawText(part.Name, tx, ty, labelFont, textPaint);
    }

    /// <summary>
    /// Draws the classic Mac 3-px bottom-and-right drop shadow for a rounded-rect button.
    /// The shadow is drawn FIRST so the button fill paints over the top-left overlap.
    /// </summary>
    private static void DrawRoundRectShadow(SKCanvas canvas, SKRect rect, float radius)
    {
        const float sh = 3f;
        using var shadowPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = ButtonBorderColor, IsAntialias = false };
        var shadowRect = new SKRect(rect.Left + sh, rect.Top + sh, rect.Right + sh, rect.Bottom + sh);
        canvas.DrawRoundRect(shadowRect, radius, radius, shadowPaint);
    }

    /// <summary>
    /// Draws a placeholder right-pointing arrow icon centred in the given square area.
    /// Used only when the ICON resource is not available in the loaded resource fork.
    /// </summary>
    private static void DrawPlaceholderIcon(SKCanvas canvas, float x, float y, float size)
    {
        // Arrow: a right-facing triangle occupying roughly the inner 60% of the icon square.
        float pad = size * 0.2f;
        float ax = x + pad;
        float ay = y + size / 2f;       // tip left-center
        float bx = x + size - pad;
        float by = y + size / 2f;       // tip right (arrowhead point)
        float headH = size * 0.35f;     // half-height of arrowhead

        using var path = new SKPath();
        // Arrowhead triangle
        path.MoveTo(bx,           by);
        path.LineTo(bx - headH,   by - headH);
        path.LineTo(bx - headH,   by - headH * 0.4f);
        // Shaft
        path.LineTo(ax,           by - headH * 0.4f);
        path.LineTo(ax,           by + headH * 0.4f);
        path.LineTo(bx - headH,   by + headH * 0.4f);
        path.LineTo(bx - headH,   by + headH);
        path.Close();

        // Hollow outline arrow — matches HyperCard's icon style (1-bit outline, white interior)
        using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = SKColors.White, IsAntialias = false };
        canvas.DrawPath(path, fillPaint);
        using var strokePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = SKColors.Black,
            StrokeWidth = Math.Max(1f, size * 0.04f),
            IsAntialias = false
        };
        canvas.DrawPath(path, strokePaint);
    }
}
