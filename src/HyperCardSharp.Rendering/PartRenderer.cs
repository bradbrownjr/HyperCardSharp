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
        (int Left, int Top, int Right, int Bottom)? wobaImgRect = null,
        HyperCardSharp.Core.Stack.StyleTableBlock? styleTable = null,
        HyperCardSharp.Core.Stack.FontTableBlock? fontTable = null)
    {
        // Build a lookup of card-specific content for background parts.
        // Per the HC format: in card.PartContents, POSITIVE IDs = background parts (per-card override).
        var cardBgContentFull = card.PartContents
            .Where(pc => pc.PartId > 0)
            .ToDictionary(pc => pc.PartId, pc => pc);

        // Shared background content (same on every card that uses this background)
        var sharedBgContentFull = bg.PartContents
            .ToDictionary(pc => pc.PartId, pc => pc);

        foreach (var part in bg.Parts)
        {
            if (!part.Visible) continue;

            if (part.IsField)
            {
                DrawFieldBackground(canvas, part);

                // Card-specific content takes priority over shared background content.
                HyperCardSharp.Core.Parts.PartContent? content =
                    cardBgContentFull.TryGetValue(part.PartId, out var cc) ? cc :
                    sharedBgContentFull.TryGetValue(part.PartId, out var sc) ? sc : null;

                if (content != null && !string.IsNullOrEmpty(content.Text))
                    TextRenderer.DrawFieldText(canvas, part, content, styleTable, fontTable);

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
        (int Left, int Top, int Right, int Bottom)? wobaImgRect = null,
        HyperCardSharp.Core.Stack.StyleTableBlock? styleTable = null,
        HyperCardSharp.Core.Stack.FontTableBlock? fontTable = null)
    {
        // Per the HC format: in card.PartContents, NEGATIVE IDs = card-local parts (key = -partId).
        var contentLookup = card.PartContents
            .Where(pc => pc.PartId < 0)
            .ToDictionary(pc => (short)(-pc.PartId), pc => pc);

        foreach (var part in card.Parts)
        {
            if (!part.Visible) continue;

            if (part.IsField)
            {
                DrawFieldBackground(canvas, part);

                if (contentLookup.TryGetValue(part.PartId, out var content) && !string.IsNullOrEmpty(content.Text))
                    TextRenderer.DrawFieldText(canvas, part, content, styleTable, fontTable);

                if (part.Style == PartStyle.Scrolling)
                    RenderScrollbarChrome(canvas, part);
            }
            else if (part.IsButton)
            {
                RenderButtonChrome(canvas, part, icons, wobaImgRect);
            }
        }
    }

    // ── Field background rendering ────────────────────────────────────────────

    /// <summary>
    /// Fills the field rectangle with white and draws the appropriate border.
    /// HyperCard renders field backgrounds dynamically — they are NOT encoded in
    /// the WOBA bitmap. Transparent fields are skipped (no fill, no border).
    /// </summary>
    private static void DrawFieldBackground(SKCanvas canvas, Part part)
    {
        if (part.Style == PartStyle.Transparent) return;
        if (part.Width <= 0 || part.Height <= 0) return;

        var rect = new SKRect(part.Left, part.Top, part.Right, part.Bottom);

        using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = SKColors.White };
        canvas.DrawRect(rect, fillPaint);

        switch (part.Style)
        {
            case PartStyle.Rectangle:
            case PartStyle.Scrolling:
            {
                using var borderPaint = new SKPaint
                    { Style = SKPaintStyle.Stroke, Color = SKColors.Black, StrokeWidth = 1 };
                canvas.DrawRect(rect, borderPaint);
                break;
            }
            case PartStyle.Shadow:
            {
                using var borderPaint = new SKPaint
                    { Style = SKPaintStyle.Stroke, Color = SKColors.Black, StrokeWidth = 1 };
                using var shadowPaint = new SKPaint
                    { Style = SKPaintStyle.Fill, Color = SKColors.Black };
                canvas.DrawRect(rect, borderPaint);
                canvas.DrawRect(new SKRect(rect.Left + 3, rect.Bottom,  rect.Right + 3, rect.Bottom + 3), shadowPaint);
                canvas.DrawRect(new SKRect(rect.Right,    rect.Top + 3, rect.Right + 3, rect.Bottom + 3), shadowPaint);
                break;
            }
            // Opaque: white fill only, no border
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

        using var whiteFill   = new SKPaint { Style = SKPaintStyle.Fill,   Color = SKColors.White };
        using var blackBorder = new SKPaint { Style = SKPaintStyle.Stroke, Color = SKColors.Black, StrokeWidth = 1 };
        using var blackFill   = new SKPaint { Style = SKPaintStyle.Fill,   Color = SKColors.Black };

        // --- 50% dithered gray track (2×2 checkerboard = classic Mac 1-bit gray) ---
        using var dithBmp = new SKBitmap(2, 2, SKColorType.Bgra8888, SKAlphaType.Opaque);
        dithBmp.SetPixel(0, 0, SKColors.Black);
        dithBmp.SetPixel(1, 0, SKColors.White);
        dithBmp.SetPixel(0, 1, SKColors.White);
        dithBmp.SetPixel(1, 1, SKColors.Black);
        using var dithShader = SKShader.CreateBitmap(dithBmp, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
        using var grayFill   = new SKPaint { Shader = dithShader, IsAntialias = false };

        const float arrowH = 15f;

        // Outer border for entire scrollbar rail (left edge only — right and top/bottom share field border)
        canvas.DrawLine(left, top, left, bottom, blackBorder);

        // Gray dithered track between the two arrow boxes
        var dithRect = new SKRect(left, top + arrowH, right, bottom - arrowH);
        canvas.DrawRect(dithRect, grayFill);

        // --- Arrow boxes: white fill + black top/bottom border lines.
        // System 7 B&W: white square, small centered solid black triangle.
        // Triangle sizes: 5px tall, 7px base — authentic to 72-dpi Mac proportions.

        // Up arrow box
        var upBox = new SKRect(left, top, right, top + arrowH);
        canvas.DrawRect(upBox, whiteFill);
        canvas.DrawLine(left, top + arrowH, right, top + arrowH, blackBorder); // bottom separator
        float cx = left + ScrollbarWidth / 2f;
        float upCy = top + arrowH / 2f;
        using var upPath = new SKPath();
        upPath.MoveTo(cx,       upCy - 3f);   // tip
        upPath.LineTo(cx - 3f,  upCy + 2f);   // bottom-left
        upPath.LineTo(cx + 3f,  upCy + 2f);   // bottom-right
        upPath.Close();
        canvas.DrawPath(upPath, blackFill);

        // Down arrow box
        var downBox = new SKRect(left, bottom - arrowH, right, bottom);
        canvas.DrawRect(downBox, whiteFill);
        canvas.DrawLine(left, bottom - arrowH, right, bottom - arrowH, blackBorder); // top separator
        float downCy = bottom - arrowH / 2f;
        using var downPath = new SKPath();
        downPath.MoveTo(cx,       downCy + 3f);  // tip
        downPath.LineTo(cx - 3f,  downCy - 2f);  // top-left
        downPath.LineTo(cx + 3f,  downCy - 2f);  // top-right
        downPath.Close();
        canvas.DrawPath(downPath, blackFill);

        // --- Thumb: white fill with black border, full track width.
        // Position reflects actual scroll ratio; fixed at top when MaxScrollY == 0.
        float thumbAreaTop = top + arrowH + 1;
        float thumbAreaH   = bottom - arrowH - arrowH - 2;
        if (thumbAreaH > 6)
        {
            float thumbH = Math.Max(8f, Math.Min(thumbAreaH * 0.2f, 20f));
            float scrollRatio = part.MaxScrollY > 0
                ? Math.Clamp(part.ScrollOffsetY / part.MaxScrollY, 0f, 1f)
                : 0f;
            float thumbTop = thumbAreaTop + scrollRatio * (thumbAreaH - thumbH);
            var thumbRect = new SKRect(left, thumbTop, right, thumbTop + thumbH);
            canvas.DrawRect(thumbRect, whiteFill);
            canvas.DrawRect(thumbRect, blackBorder);
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
                // White fill, no border. (Still gets icon/label/hilite below --
                // previously this returned immediately and never drew a label, which
                // was a bug: HyperCard opaque buttons do show their name.)
                canvas.DrawRect(rect, fillPaint);
                break;

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

            case PartStyle.CheckBox:
                RenderCheckBoxChrome(canvas, part, rect);
                return;

            case PartStyle.RadioButton:
                RenderRadioButtonChrome(canvas, part, rect);
                return;

            case PartStyle.Popup:
                RenderPopupChrome(canvas, part, rect);
                return;

            default:  // Rectangle, and any unexpected style (e.g. Scrolling on a button)
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
        if (part.ShowName && !string.IsNullOrEmpty(part.Name))
        {
            using var typeface = FontMapper.GetTypeface(part.TextFontId, part.TextStyle);

            // Auto-shrink font size if label is wider than the button (minimum 9pt).
            // 6px total horizontal margin (3px each side) matches HyperCard's Chicago font metrics.
            float labelMaxWidth = rect.Width - 6f;
            float effectiveSize = ShrinkFontToFit(typeface, part.Name, textSize, labelMaxWidth);

            using var labelFont = FontMapper.CreateAliasedFont(typeface, effectiveSize);
            using var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = false };

            float tw = labelFont.MeasureText(part.Name);
            float tx = rect.MidX - tw / 2f;

            // Vertical position: centre in remaining text area
            float remainH = rect.Bottom - textAreaTop;
            float ty = textAreaTop + remainH / 2f + effectiveSize / 2f - 1f;

            canvas.DrawText(part.Name, tx, ty, labelFont, textPaint);
        }

        // ─ Hilite (push-button styles only) ─────────────────────────────────────
        // Authentic 1-bit "pressed" look: invert every pixel drawn so far (fill,
        // border, icon, and label) within the button's shape. CheckBox/RadioButton/
        // Popup return earlier above and never reach here -- they show their
        // check/dot mark instead of inverting (see their own methods below).
        if (part.HiliteState)
            ApplyHiliteInversion(canvas, rect, part.Style);
    }

    /// <summary>
    /// Shrinks a font size (down to a 9pt floor) until <paramref name="text"/> fits within
    /// <paramref name="maxWidth"/>. Shared by the centered push-button label and the
    /// left-aligned checkbox/radio-button/popup labels.
    /// </summary>
    private static float ShrinkFontToFit(SKTypeface typeface, string text, float baseSize, float maxWidth)
    {
        float size = baseSize;
        while (size > 9f)
        {
            using var probe = FontMapper.CreateAliasedFont(typeface, size);
            if (probe.MeasureText(text) <= maxWidth)
                break;
            size -= 1f;
        }
        return size;
    }

    /// <summary>
    /// Inverts every pixel within the button's shape using SKBlendMode.Difference against
    /// a solid white fill -- this maps black&lt;-&gt;white exactly with no intermediate
    /// gray, matching the project's authentic 1-bit rendering requirement.
    /// </summary>
    private static void ApplyHiliteInversion(SKCanvas canvas, SKRect rect, PartStyle style)
    {
        canvas.Save();
        switch (style)
        {
            case PartStyle.Oval:
                using (var ovalPath = new SKPath())
                {
                    ovalPath.AddOval(rect);
                    canvas.ClipPath(ovalPath);
                }
                break;

            case PartStyle.RoundRect:
            case PartStyle.Standard:
            case PartStyle.Default:
                using (var roundPath = new SKPath())
                {
                    roundPath.AddRoundRect(rect, ButtonCornerRadius, ButtonCornerRadius);
                    canvas.ClipPath(roundPath);
                }
                break;

            default:
                canvas.ClipRect(rect);
                break;
        }

        using var invertPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = SKColors.White,
            BlendMode = SKBlendMode.Difference
        };
        canvas.DrawRect(rect, invertPaint);
        canvas.Restore();
    }

    private const float CheckGlyphSize = 12f;

    /// <summary>
    /// CheckBox chrome: a small square at the left edge (white fill, black border),
    /// an "X" mark drawn inside when hilited (classic Mac System 6/7 checkbox style --
    /// not a modern checkmark tick), and the label left-aligned to its right.
    /// Hilite here does NOT invert the whole part (unlike push-button styles) --
    /// only the mark inside the box changes.
    /// </summary>
    private static void RenderCheckBoxChrome(SKCanvas canvas, Part part, SKRect rect)
    {
        float boxTop = rect.Top + (rect.Height - CheckGlyphSize) / 2f;
        float boxLeft = rect.Left + 2f;
        var boxRect = new SKRect(boxLeft, boxTop, boxLeft + CheckGlyphSize, boxTop + CheckGlyphSize);

        using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = SKColors.White };
        using var borderPaint = new SKPaint
            { Style = SKPaintStyle.Stroke, Color = ButtonBorderColor, StrokeWidth = 1, IsAntialias = false };
        canvas.DrawRect(boxRect, fillPaint);
        canvas.DrawRect(boxRect, borderPaint);

        if (part.HiliteState)
        {
            using var markPaint = new SKPaint
                { Style = SKPaintStyle.Stroke, Color = SKColors.Black, StrokeWidth = 1.5f, IsAntialias = false };
            const float pad = 2.5f;
            canvas.DrawLine(boxRect.Left + pad, boxRect.Top + pad, boxRect.Right - pad, boxRect.Bottom - pad, markPaint);
            canvas.DrawLine(boxRect.Left + pad, boxRect.Bottom - pad, boxRect.Right - pad, boxRect.Top + pad, markPaint);
        }

        DrawLeftAlignedLabel(canvas, part, boxRect.Right + 4f, rect);
    }

    /// <summary>
    /// RadioButton chrome: a small circle at the left edge (white fill, black border),
    /// a filled center dot drawn when hilited, and the label left-aligned to its right.
    /// Like CheckBox, hilite does not invert the whole part.
    /// </summary>
    private static void RenderRadioButtonChrome(SKCanvas canvas, Part part, SKRect rect)
    {
        float circleTop = rect.Top + (rect.Height - CheckGlyphSize) / 2f;
        float circleLeft = rect.Left + 2f;
        var circleRect = new SKRect(circleLeft, circleTop, circleLeft + CheckGlyphSize, circleTop + CheckGlyphSize);

        using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = SKColors.White };
        using var borderPaint = new SKPaint
            { Style = SKPaintStyle.Stroke, Color = ButtonBorderColor, StrokeWidth = 1, IsAntialias = false };
        canvas.DrawOval(circleRect, fillPaint);
        canvas.DrawOval(circleRect, borderPaint);

        if (part.HiliteState)
        {
            using var dotPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = SKColors.Black, IsAntialias = false };
            float inset = CheckGlyphSize * 0.28f;
            var dotRect = new SKRect(circleRect.Left + inset, circleRect.Top + inset,
                circleRect.Right - inset, circleRect.Bottom - inset);
            canvas.DrawOval(dotRect, dotPaint);
        }

        DrawLeftAlignedLabel(canvas, part, circleRect.Right + 4f, rect);
    }

    /// <summary>
    /// Popup chrome: a plain rectangular frame, the button's name drawn left-aligned
    /// within the "title width" region (<see cref="Part.TitleWidthOrLastSelectedLine"/>,
    /// with a vertical divider line at its edge when set), and a solid downward-pointing
    /// triangle near the right edge indicating a drop-down menu.
    /// </summary>
    private static void RenderPopupChrome(SKCanvas canvas, Part part, SKRect rect)
    {
        using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = SKColors.White };
        using var borderPaint = new SKPaint
            { Style = SKPaintStyle.Stroke, Color = ButtonBorderColor, StrokeWidth = 1, IsAntialias = false };
        canvas.DrawRect(rect, fillPaint);
        canvas.DrawRect(rect, borderPaint);

        const float arrowAreaWidth = 16f;
        float maxTitleWidth = Math.Max(0f, rect.Width - arrowAreaWidth);
        bool hasTitleWidth = part.TitleWidthOrLastSelectedLine > 0;
        float titleWidth = hasTitleWidth
            ? Math.Min(part.TitleWidthOrLastSelectedLine, maxTitleWidth)
            : maxTitleWidth;

        if (hasTitleWidth && titleWidth < maxTitleWidth)
        {
            float dividerX = rect.Left + titleWidth;
            canvas.DrawLine(dividerX, rect.Top, dividerX, rect.Bottom, borderPaint);
        }

        if (part.ShowName && !string.IsNullOrEmpty(part.Name))
        {
            using var typeface = FontMapper.GetTypeface(part.TextFontId, part.TextStyle);
            float textSize = part.TextSize > 0 ? part.TextSize : 12f;
            float maxWidth = Math.Max(0f, titleWidth - 6f);
            float effectiveSize = ShrinkFontToFit(typeface, part.Name, textSize, maxWidth);
            using var labelFont = FontMapper.CreateAliasedFont(typeface, effectiveSize);
            using var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = false };
            float ty = rect.MidY + effectiveSize / 2f - 1f;
            canvas.DrawText(part.Name, rect.Left + 3f, ty, labelFont, textPaint);
        }

        float arrowCx = rect.Right - arrowAreaWidth / 2f;
        float arrowCy = rect.MidY;
        using var arrowFill = new SKPaint { Style = SKPaintStyle.Fill, Color = SKColors.Black, IsAntialias = false };
        using var arrowPath = new SKPath();
        arrowPath.MoveTo(arrowCx - 4f, arrowCy - 2f);
        arrowPath.LineTo(arrowCx + 4f, arrowCy - 2f);
        arrowPath.LineTo(arrowCx, arrowCy + 3f);
        arrowPath.Close();
        canvas.DrawPath(arrowPath, arrowFill);
    }

    /// <summary>
    /// Draws a button's name left-aligned starting at <paramref name="textLeft"/>,
    /// vertically centred within <paramref name="rect"/>. Used by CheckBox and
    /// RadioButton, whose labels sit to the right of a small glyph rather than
    /// centered like push buttons.
    /// </summary>
    private static void DrawLeftAlignedLabel(SKCanvas canvas, Part part, float textLeft, SKRect rect)
    {
        if (!part.ShowName || string.IsNullOrEmpty(part.Name)) return;

        using var typeface = FontMapper.GetTypeface(part.TextFontId, part.TextStyle);
        float textSize = part.TextSize > 0 ? part.TextSize : 12f;
        float maxWidth = Math.Max(0f, rect.Right - 3f - textLeft);
        float effectiveSize = ShrinkFontToFit(typeface, part.Name, textSize, maxWidth);

        using var labelFont = FontMapper.CreateAliasedFont(typeface, effectiveSize);
        using var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = false };
        float ty = rect.MidY + effectiveSize / 2f - 1f;
        canvas.DrawText(part.Name, textLeft, ty, labelFont, textPaint);
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
