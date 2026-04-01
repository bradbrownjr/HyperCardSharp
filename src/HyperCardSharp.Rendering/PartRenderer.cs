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
            // Scale the icon to fit inside the button with 2px padding.
            const float iconPad = 2f;
            float iconAreaH = part.ShowName && !string.IsNullOrEmpty(part.Name)
                ? rect.Height * 0.65f
                : rect.Height - iconPad * 2;
            float iconSize = Math.Min(rect.Width - iconPad * 2, iconAreaH);
            float iconX = rect.MidX - iconSize / 2f;
            float iconY = rect.Top + iconPad;

            if (icons != null && icons.TryGetValue(part.IconId, out var iconBitmap))
            {
                var destRect = new SKRect(iconX, iconY, iconX + iconSize, iconY + iconSize);
                canvas.DrawBitmap(iconBitmap, destRect);
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
        using var labelFont = new SKFont(typeface, textSize);
        using var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = false };

        float tw = labelFont.MeasureText(part.Name);
        float tx = rect.MidX - tw / 2f;

        // Vertical position: centre in remaining text area
        float remainH = rect.Bottom - textAreaTop;
        float ty = textAreaTop + remainH / 2f + textSize / 2f - 1f;

        canvas.DrawText(part.Name, tx, ty, labelFont, textPaint);
    }

    /// <summary>
    /// Draws a placeholder right-pointing arrow icon centred in the given square area.
    /// Replaces a real ICON resource until icon parsing is implemented.
    /// The arrow matches the HyperCard default "navigate-forward" icon appearance.
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

        using var iconPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = SKColors.Black, IsAntialias = true };
        canvas.DrawPath(path, iconPaint);
    }
}
