using HyperCardSharp.Core.Parts;
using SkiaSharp;

namespace HyperCardSharp.Rendering;

/// <summary>
/// Renders text content into HyperCard field areas using SkiaSharp.
/// Uses the SkiaSharp 3.x SKFont API (SKPaint text properties are deprecated).
/// Handles multi-line text, Mac font mapping, and the HyperCard text-style byte.
/// </summary>
public static class TextRenderer
{
    private const int FieldPadding = 2; // left/right inset inside field rect (points)

    /// <summary>
    /// Draws text into the bounding rect of the given part.
    /// Clips output to the part's rect so text never overflows.
    /// </summary>
    public static void DrawFieldText(SKCanvas canvas, Part part, string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        var rect = new SKRect(part.Left, part.Top, part.Right, part.Bottom);
        if (rect.Width <= 0 || rect.Height <= 0) return;

        using var typeface = FontMapper.GetTypeface(part.TextFontId, part.TextStyle);
        float textSize = part.TextSize > 0 ? part.TextSize : 12f;
        float lineHeight = part.TextHeight > 0 ? part.TextHeight : textSize * 1.2f;

        using var font = new SKFont(typeface, textSize);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = false };

        // HyperCard TextAlign: 0=left, 1=center, -1=right (stored as short)
        var align = (HcTextAlign)part.TextAlign;

        canvas.Save();
        canvas.ClipRect(rect);

        DrawLines(canvas, font, paint, text, part.TextStyle, rect, align, lineHeight, textSize);

        canvas.Restore();
    }

    private static void DrawLines(
        SKCanvas canvas, SKFont font, SKPaint paint, string text, byte textStyle,
        SKRect rect, HcTextAlign align, float lineHeight, float textSize)
    {
        var rawLines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        float x0 = rect.Left + FieldPadding;
        float xRight = rect.Right - FieldPadding;
        float availWidth = xRight - x0;

        // Y baseline: start one line-height below the top
        float y = rect.Top + textSize;

        foreach (var rawLine in rawLines)
        {
            foreach (var wrappedLine in WrapLine(rawLine, font, availWidth))
            {
                if (y > rect.Bottom) return;

                float tw = font.MeasureText(wrappedLine);
                float x = align switch
                {
                    HcTextAlign.Center => (x0 + xRight) / 2 - tw / 2,
                    HcTextAlign.Right  => xRight - tw,
                    _                  => x0,
                };

                var skAlign = align switch
                {
                    HcTextAlign.Center => SKTextAlign.Center,
                    HcTextAlign.Right  => SKTextAlign.Right,
                    _                  => SKTextAlign.Left,
                };

                canvas.DrawText(wrappedLine, x, y, skAlign, font, paint);

                // Underline (bit 2 of textStyle)
                if ((textStyle & 0x04) != 0)
                {
                    using var linePaint = new SKPaint { Color = SKColors.Black, StrokeWidth = 1 };
                    canvas.DrawLine(x, y + 1, x + tw, y + 1, linePaint);
                }

                y += lineHeight;
            }
        }
    }

    /// <summary>Word-wraps a single source line to fit within availWidth.</summary>
    private static IEnumerable<string> WrapLine(string line, SKFont font, float availWidth)
    {
        if (string.IsNullOrEmpty(line))
        {
            yield return "";
            yield break;
        }

        if (font.MeasureText(line) <= availWidth)
        {
            yield return line;
            yield break;
        }

        var words = line.Split(' ');
        var current = new System.Text.StringBuilder();

        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (font.MeasureText(candidate) <= availWidth)
            {
                current.Clear();
                current.Append(candidate);
            }
            else
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
                // Word wider than available space — emit as-is (canvas clipping handles overflow)
                if (font.MeasureText(word) > availWidth)
                    yield return word;
                else
                    current.Append(word);
            }
        }

        if (current.Length > 0)
            yield return current.ToString();
    }

    private enum HcTextAlign
    {
        Left   = 0,
        Center = 1,
        Right  = -1,
    }
}
