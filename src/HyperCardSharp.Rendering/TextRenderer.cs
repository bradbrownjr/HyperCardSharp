using HyperCardSharp.Core.Parts;
using HyperCardSharp.Core.Stack;
using SkiaSharp;

namespace HyperCardSharp.Rendering;

/// <summary>
/// Renders text content into HyperCard field areas using SkiaSharp.
/// Uses the SkiaSharp 3.x SKFont API (SKPaint text properties are deprecated).
/// Handles multi-line text, styled runs, Mac font mapping, and the HyperCard text-style byte.
/// </summary>
public static class TextRenderer
{
    private const int FieldPadding = 2; // left/right inset inside field rect (points)

    // ── Public entrypoints ────────────────────────────────────────────────────

    /// <summary>
    /// Draws field text using full styled-run information from the parsed stack.
    /// Falls back to the Part's base font when no style runs are present or the
    /// StyleTableBlock is unavailable.
    /// </summary>
    public static void DrawFieldText(
        SKCanvas canvas,
        Part part,
        PartContent content,
        StyleTableBlock? styleTable = null,
        FontTableBlock? fontTable = null)
    {
        if (string.IsNullOrEmpty(content.Text)) return;

        var rect = new SKRect(part.Left, part.Top, part.Right, part.Bottom);
        if (rect.Width <= 0 || rect.Height <= 0) return;

        const float scrollbarWidth = 15f;
        if (part.Style == PartStyle.Scrolling)
            rect = new SKRect(rect.Left, rect.Top, rect.Right - scrollbarWidth, rect.Bottom);

        var align = (HcTextAlign)part.TextAlign;

        canvas.Save();
        canvas.ClipRect(rect);

        if (content.HasStyles && styleTable != null)
            DrawStyledText(canvas, part, content.Text, content.StyleRuns, styleTable, fontTable, rect, align);
        else
            DrawPlainText(canvas, part, content.Text, fontTable, rect, align);

        canvas.Restore();
    }

    /// <summary>Plain-text overload for callers that only have a string (no PartContent).</summary>
    public static void DrawFieldText(SKCanvas canvas, Part part, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var rect = new SKRect(part.Left, part.Top, part.Right, part.Bottom);
        if (rect.Width <= 0 || rect.Height <= 0) return;

        const float scrollbarWidth = 15f;
        if (part.Style == PartStyle.Scrolling)
            rect = new SKRect(rect.Left, rect.Top, rect.Right - scrollbarWidth, rect.Bottom);

        canvas.Save();
        canvas.ClipRect(rect);
        DrawPlainText(canvas, part, text, null, rect, (HcTextAlign)part.TextAlign);
        canvas.Restore();
    }

    // ── Plain-text path (no style runs) ──────────────────────────────────────

    private static void DrawPlainText(
        SKCanvas canvas, Part part, string text, FontTableBlock? fontTable,
        SKRect rect, HcTextAlign align)
    {
        using var typeface = FontMapper.GetTypeface(part.TextFontId, part.TextStyle, fontTable);
        float textSize   = part.TextSize > 0 ? part.TextSize : 12f;
        float lineHeight = part.TextHeight > 0 ? part.TextHeight : textSize * 1.2f;

        using var font  = new SKFont(typeface, textSize);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = false };

        DrawLines(canvas, font, paint, text, part.TextStyle, rect, align, lineHeight, textSize);
    }

    private static void DrawLines(
        SKCanvas canvas, SKFont font, SKPaint paint, string text, byte textStyle,
        SKRect rect, HcTextAlign align, float lineHeight, float textSize)
    {
        var rawLines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        float x0 = rect.Left + FieldPadding;
        float xRight = rect.Right - FieldPadding;
        float availWidth = xRight - x0;

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

                canvas.DrawText(wrappedLine, x, y, SKTextAlign.Left, font, paint);

                if ((textStyle & 0x04) != 0) // underline
                {
                    using var linePaint = new SKPaint { Color = SKColors.Black, StrokeWidth = 1 };
                    canvas.DrawLine(x, y + 1, x + tw, y + 1, linePaint);
                }

                y += lineHeight;
            }
        }
    }

    // ── Styled-run path ───────────────────────────────────────────────────────

    // A resolved run: a text span with its resolved font and style flags.
    private readonly record struct ResolvedRun(int CharFrom, int CharTo, float FontSize, byte StyleFlags, int StyleIdx);

    private static void DrawStyledText(
        SKCanvas canvas,
        Part part,
        string text,
        IReadOnlyList<StyleRun> styleRuns,
        StyleTableBlock styleTable,
        FontTableBlock? fontTable,
        SKRect rect,
        HcTextAlign align)
    {
        // Build a flat list of resolved runs covering the entire text
        var runs = BuildResolvedRuns(text, styleRuns, part, styleTable);

        float x0         = rect.Left + FieldPadding;
        float xRight     = rect.Right - FieldPadding;
        float availWidth = xRight - x0;

        // Normalise line endings, split into logical lines, tracking absolute char offsets
        var normalised = text.Replace("\r\n", "\n").Replace('\r', '\n');
        int lineStart = 0;
        float y = rect.Top;

        while (lineStart <= normalised.Length)
        {
            if (y > rect.Bottom) return;

            int nlIdx = normalised.IndexOf('\n', lineStart);
            int lineEnd = nlIdx < 0 ? normalised.Length : nlIdx;

            // Determine the visual line height for this logical line (= max font size in runs)
            float lineHeight   = GetLineHeight(runs, lineStart, lineEnd, part);
            float baseFontSize = part.TextSize > 0 ? part.TextSize : 12f;
            y += lineHeight > 0 ? lineHeight : baseFontSize;

            // Collect word-wrapped visual lines for this logical line
            var tokenGroups = BuildTokenGroups(normalised, lineStart, lineEnd, runs, part, styleTable, fontTable);

            // Wrap tokenGroups onto visual lines
            var visualLines = WrapTokenGroups(tokenGroups, availWidth);

            // Render each visual line.
            // y was already advanced for the FIRST visual line of this logical line (above).
            // For additional wrapped lines, advance y BEFORE drawing so they don't overlap.
            for (int vli = 0; vli < visualLines.Count; vli++)
            {
                if (vli > 0) y += lineHeight; // advance before each wrapped continuation line
                if (y > rect.Bottom) break;
                RenderVisualLine(canvas, visualLines[vli], align, x0, xRight, y, part, styleTable, fontTable);
            }

            lineStart = lineEnd + 1; // skip the '\n'
        }
    }

    // A token: a word or space chunk with the style table index that applies to it (-1 = use part defaults)
    private readonly record struct StyledToken(string Text, float Width, int StyleIdx);

    private static float GetLineHeight(
        IReadOnlyList<ResolvedRun> runs, int lineStart, int lineEnd, Part part)
    {
        float max = 0;
        foreach (var run in runs)
        {
            if (run.CharFrom >= lineEnd || run.CharTo <= lineStart) continue;
            float sz = run.FontSize;
            if (sz > max) max = sz;
        }
        float lineHeight = max > 0 ? max * 1.2f : (part.TextSize > 0 ? part.TextSize : 12f) * 1.2f;
        if (part.TextHeight > 0) lineHeight = part.TextHeight;
        return lineHeight;
    }

    private static List<StyledToken> BuildTokenGroups(
        string text, int lineStart, int lineEnd,
        IReadOnlyList<ResolvedRun> runs, Part part,
        StyleTableBlock styleTable, FontTableBlock? fontTable)
    {
        var tokens = new List<StyledToken>();
        if (lineStart >= lineEnd)
        {
            tokens.Add(new StyledToken("", 0f, -1));
            return tokens;
        }

        // Walk character by character; emit a token when the active run changes or on space boundaries
        int i = lineStart;
        while (i < lineEnd)
        {
            // Find the active run for character i
            int runIdx  = FindRunIndexAt(runs, i);
            int styleIdx = runIdx >= 0 ? runs[runIdx].StyleIdx : -1;
            int runEnd  = runIdx >= 0 ? Math.Min(runs[runIdx].CharTo, lineEnd) : lineEnd;

            // Within this run, split further at space boundaries (word wrap requires it)
            int tokenStart = i;
            while (i < runEnd)
            {
                bool isSpace = text[i] == ' ';
                // Advance until character type changes or run ends
                while (i < runEnd && (text[i] == ' ') == isSpace)
                    i++;

                string tokenText = text.Substring(tokenStart, i - tokenStart);
                float tw = MeasureTokenWidth(tokenText, styleIdx, part, styleTable, fontTable);
                tokens.Add(new StyledToken(tokenText, tw, styleIdx));
                tokenStart = i;
            }
        }

        return tokens;
    }

    private static float MeasureTokenWidth(
        string tokenText, int styleIdx,
        Part part, StyleTableBlock styleTable, FontTableBlock? fontTable)
    {
        if (string.IsNullOrEmpty(tokenText)) return 0f;
        var entry = styleIdx >= 0 ? styleTable.GetStyle(styleIdx) : null;
        var typeface = entry != null
            ? FontMapper.GetTypefaceForRun(entry, part, fontTable)
            : FontMapper.GetTypeface(part.TextFontId, part.TextStyle, fontTable);
        using var tf  = typeface;
        float sz = entry != null ? FontMapper.GetSizeForRun(entry, part) : (part.TextSize > 0 ? part.TextSize : 12f);
        using var f  = new SKFont(tf, sz);
        return f.MeasureText(tokenText);
    }

    private static int FindRunIndexAt(IReadOnlyList<ResolvedRun> runs, int charIdx)
    {
        for (int i = runs.Count - 1; i >= 0; i--)
            if (runs[i].CharFrom <= charIdx && charIdx < runs[i].CharTo)
                return i;
        return -1;
    }

    private static List<List<StyledToken>> WrapTokenGroups(List<StyledToken> tokens, float availWidth)
    {
        var result = new List<List<StyledToken>>();
        var current = new List<StyledToken>();
        float lineWidth = 0;

        foreach (var token in tokens)
        {
            if (current.Count == 0)
            {
                // Always start a new line with the first token (even if it's too wide)
                current.Add(token);
                lineWidth = token.Width;
            }
            else if (lineWidth + token.Width <= availWidth || token.Text == " " || current.Count == 0)
            {
                current.Add(token);
                lineWidth += token.Width;
            }
            else
            {
                // Wrap: trim trailing space from previous visual line
                while (current.Count > 0 && current[^1].Text == " ")
                    current.RemoveAt(current.Count - 1);
                result.Add(current);
                current = new List<StyledToken>();
                // Skip leading spaces on the new line
                if (token.Text != " ")
                {
                    current.Add(token);
                    lineWidth = token.Width;
                }
                else
                {
                    lineWidth = 0;
                }
            }
        }

        if (current.Count > 0) result.Add(current);
        if (result.Count == 0) result.Add(new List<StyledToken>());
        return result;
    }

    private static void RenderVisualLine(
        SKCanvas canvas,
        List<StyledToken> tokens,
        HcTextAlign align,
        float x0, float xRight, float y,
        Part part, StyleTableBlock styleTable, FontTableBlock? fontTable)
    {
        if (tokens.Count == 0) return;

        float lineWidth = tokens.Sum(t => t.Width);

        float startX = align switch
        {
            HcTextAlign.Center => (x0 + xRight) / 2 - lineWidth / 2,
            HcTextAlign.Right  => xRight - lineWidth,
            _                  => x0,
        };

        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = false };
        float curX = startX;

        foreach (var token in tokens)
        {
            if (string.IsNullOrEmpty(token.Text)) continue;

            var entry      = token.StyleIdx >= 0 ? styleTable.GetStyle(token.StyleIdx) : null;
            SKTypeface? tf = entry != null
                ? FontMapper.GetTypefaceForRun(entry, part, fontTable)
                : FontMapper.GetTypeface(part.TextFontId, part.TextStyle, fontTable);
            float fontSize  = entry != null ? FontMapper.GetSizeForRun(entry, part)       : (part.TextSize > 0 ? part.TextSize : 12f);
            byte styleFlags = entry != null ? FontMapper.GetStyleFlagsForRun(entry, part)  : part.TextStyle;

            using var typeface = tf;
            using var fnt      = new SKFont(typeface ?? SKTypeface.Default, fontSize);
            float tw = fnt.MeasureText(token.Text);

            canvas.DrawText(token.Text, curX, y, SKTextAlign.Left, fnt, paint);

            if ((styleFlags & 0x04) != 0) // underline
            {
                using var lp = new SKPaint { Color = SKColors.Black, StrokeWidth = 1 };
                canvas.DrawLine(curX, y + 1, curX + tw, y + 1, lp);
            }

            curX += tw;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IReadOnlyList<ResolvedRun> BuildResolvedRuns(
        string text, IReadOnlyList<StyleRun> styleRuns, Part part, StyleTableBlock styleTable)
    {
        var result = new List<ResolvedRun>(styleRuns.Count + 1);

        for (int i = 0; i < styleRuns.Count; i++)
        {
            int charFrom = styleRuns[i].CharacterPosition;
            int charTo   = (i + 1 < styleRuns.Count) ? styleRuns[i + 1].CharacterPosition : text.Length;
            charTo = Math.Min(charTo, text.Length);
            if (charFrom >= charTo) continue;

            int styleIdx = styleRuns[i].StyleId;
            var entry    = styleTable.GetStyle(styleIdx);
            float size   = entry != null ? FontMapper.GetSizeForRun(entry, part) : (part.TextSize > 0 ? part.TextSize : 12f);
            byte flags   = entry != null ? FontMapper.GetStyleFlagsForRun(entry, part) : part.TextStyle;

            result.Add(new ResolvedRun(charFrom, charTo, size, flags, styleIdx));
        }

        if (result.Count == 0)
            result.Add(new ResolvedRun(0, text.Length, part.TextSize > 0 ? part.TextSize : 12f, part.TextStyle, -1));

        return result;
    }

    /// <summary>Word-wraps a single source line to fit within availWidth (plain-text path).</summary>
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
