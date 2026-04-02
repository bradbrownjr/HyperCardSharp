using SkiaSharp;

namespace HyperCardSharp.Rendering;

/// <summary>
/// Composites two card bitmaps together to produce a single frame of a HyperCard
/// visual-effect transition.  All built-in HyperCard effects are handled here;
/// unknown effect names fall back to dissolve.
///
/// All operations are pure SkiaSharp — no Avalonia dependency.
/// </summary>
public static class TransitionRenderer
{
    // ── Dissolve support ──────────────────────────────────────────────────────
    // rank[p] = 0-based position in the reveal sequence for pixel p.
    // Built once per (width × height) and reused.
    private static (int W, int H, int[] Rank)? _dissolveCache;
    private static readonly object _dissolveLock = new();

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes a composited transition frame into <paramref name="result"/>.
    /// </summary>
    /// <param name="from">The card that is leaving the screen.</param>
    /// <param name="to">The card that is entering the screen.</param>
    /// <param name="result">Pre-allocated bitmap with the same dimensions; written in place.</param>
    /// <param name="effect">Normalised effect name (e.g. "dissolve", "scroll left").</param>
    /// <param name="t">Transition progress in [0, 1].  0 = fully from, 1 = fully to.</param>
    public static void CompositeFrame(
        SKBitmap from, SKBitmap to, SKBitmap result, string effect, float t)
    {
        // Note: t is clamped at both ends so edge-case rendering is clean.
        t = Math.Clamp(t, 0f, 1f);

        switch (effect.Trim().ToLowerInvariant())
        {
            // ── Scroll ────────────────────────────────────────────────────────
            case "scroll left":
                RenderScroll(from, to, result, t, dx: -1, dy: 0);
                break;
            case "scroll right":
                RenderScroll(from, to, result, t, dx: 1, dy: 0);
                break;
            case "scroll up":
                RenderScroll(from, to, result, t, dx: 0, dy: -1);
                break;
            case "scroll down":
                RenderScroll(from, to, result, t, dx: 0, dy: 1);
                break;

            // ── Wipe ──────────────────────────────────────────────────────────
            // Wipe: the new image sweeps in from one edge; the old image is static.
            case "wipe left":
                RenderWipe(from, to, result, t, dx: -1, dy: 0);
                break;
            case "wipe right":
                RenderWipe(from, to, result, t, dx: 1, dy: 0);
                break;
            case "wipe up":
                RenderWipe(from, to, result, t, dx: 0, dy: -1);
                break;
            case "wipe down":
                RenderWipe(from, to, result, t, dx: 0, dy: 1);
                break;

            // ── Zoom ──────────────────────────────────────────────────────────
            case "zoom open":
                RenderZoomOpen(from, to, result, t);
                break;
            case "zoom close":
                RenderZoomClose(from, to, result, t);
                break;

            // ── Iris ──────────────────────────────────────────────────────────
            case "iris open":
                RenderIrisOpen(from, to, result, t);
                break;
            case "iris close":
                RenderIrisClose(from, to, result, t);
                break;

            // ── Venetian blinds ───────────────────────────────────────────────
            case "venetian blinds":
                RenderVenetianBlinds(from, to, result, t);
                break;

            // ── Barn door ─────────────────────────────────────────────────────
            case "barn door open":
                RenderBarnDoor(from, to, result, t, open: true);
                break;
            case "barn door close":
                RenderBarnDoor(from, to, result, t, open: false);
                break;

            // ── Checkerboard ──────────────────────────────────────────────────
            case "checkerboard":
                RenderCheckerboard(from, to, result, t);
                break;

            // ── Stretch / shrink ──────────────────────────────────────────────
            case "stretch from center":
                RenderStretchFromCenter(from, to, result, t);
                break;
            case "stretch from top":
                RenderStretchFromTop(from, to, result, t);
                break;
            case "stretch from bottom":
                RenderStretchFromBottom(from, to, result, t);
                break;
            case "shrink to center":
                RenderShrinkToCenter(from, to, result, t);
                break;
            case "shrink to top":
                RenderShrinkToTop(from, to, result, t);
                break;
            case "shrink to bottom":
                RenderShrinkToBottom(from, to, result, t);
                break;

            // ── Dissolve (default / unknown) ──────────────────────────────────
            default:
                RenderDissolve(from, to, result, t);
                break;
        }
    }

    // ── Scroll ────────────────────────────────────────────────────────────────

    /// <param name="dx">-1 = scroll left, +1 = scroll right (or 0 for vertical).</param>
    /// <param name="dy">-1 = scroll up,   +1 = scroll down (or 0 for horizontal).</param>
    private static void RenderScroll(
        SKBitmap from, SKBitmap to, SKBitmap result, float t, int dx, int dy)
    {
        int w = result.Width, h = result.Height;
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Black);

        // "from" slides out; "to" slides in from the opposite edge.
        // dx=-1: from slides left (x offset = -t*w), to comes from right (offset = (1-t)*w)
        // dy=-1: analogous vertically.
        float fromX = dx * t * w;
        float fromY = dy * t * h;
        float toX   = fromX - dx * w;
        float toY   = fromY - dy * h;

        canvas.DrawBitmap(from, fromX, fromY);
        canvas.DrawBitmap(to,   toX,   toY);
    }

    // ── Wipe ──────────────────────────────────────────────────────────────────
    // Wipe: "from" stays fixed. "to" is revealed by expanding a clip rect from one edge.

    /// <param name="dx">-1 = wipe left (reveal from right), +1 = wipe right (reveal from left).</param>
    /// <param name="dy">-1 = wipe up   (reveal from bottom), +1 = wipe down (reveal from top).</param>
    private static void RenderWipe(
        SKBitmap from, SKBitmap to, SKBitmap result, float t, int dx, int dy)
    {
        int w = result.Width, h = result.Height;
        using var canvas = new SKCanvas(result);

        // Draw the old image as the full background
        canvas.DrawBitmap(from, 0, 0);

        // Clip region for the new image expands from the leading edge
        SKRect clip;
        if (dx != 0)
        {
            float revealW = w * t;
            if (dx > 0)      // wipe right: reveal from left edge
                clip = new SKRect(0, 0, revealW, h);
            else             // wipe left: reveal from right edge
                clip = new SKRect(w - revealW, 0, w, h);
        }
        else
        {
            float revealH = h * t;
            if (dy > 0)      // wipe down: reveal from top edge
                clip = new SKRect(0, 0, w, revealH);
            else             // wipe up: reveal from bottom edge
                clip = new SKRect(0, h - revealH, w, h);
        }

        canvas.Save();
        canvas.ClipRect(clip);
        canvas.DrawBitmap(to, 0, 0);
        canvas.Restore();
    }

    // ── Zoom ──────────────────────────────────────────────────────────────────

    private static void RenderZoomOpen(SKBitmap from, SKBitmap to, SKBitmap result, float t)    {
        int w = result.Width, h = result.Height;
        using var canvas = new SKCanvas(result);
        canvas.DrawBitmap(from, 0, 0);

        float sw = w * t, sh = h * t;
        float ox = (w - sw) / 2f, oy = (h - sh) / 2f;
        canvas.DrawBitmap(to, SKRect.Create(ox, oy, sw, sh));
    }

    private static void RenderZoomClose(SKBitmap from, SKBitmap to, SKBitmap result, float t)
    {
        int w = result.Width, h = result.Height;
        using var canvas = new SKCanvas(result);
        canvas.DrawBitmap(to, 0, 0);

        float sw = w * (1f - t), sh = h * (1f - t);
        float ox = (w - sw) / 2f, oy = (h - sh) / 2f;
        canvas.DrawBitmap(from, SKRect.Create(ox, oy, sw, sh));
    }

    // ── Iris ──────────────────────────────────────────────────────────────────

    private static void RenderIrisOpen(SKBitmap from, SKBitmap to, SKBitmap result, float t)
    {
        int w = result.Width, h = result.Height;
        float maxR = (float)Math.Sqrt(w * w / 4.0 + h * h / 4.0);
        using var canvas = new SKCanvas(result);
        canvas.DrawBitmap(from, 0, 0);
        canvas.Save();
        using var path = new SKPath();
        path.AddCircle(w / 2f, h / 2f, t * maxR);
        canvas.ClipPath(path);
        canvas.DrawBitmap(to, 0, 0);
        canvas.Restore();
    }

    private static void RenderIrisClose(SKBitmap from, SKBitmap to, SKBitmap result, float t)
    {
        int w = result.Width, h = result.Height;
        float maxR = (float)Math.Sqrt(w * w / 4.0 + h * h / 4.0);
        using var canvas = new SKCanvas(result);
        canvas.DrawBitmap(to, 0, 0);
        canvas.Save();
        using var path = new SKPath();
        path.AddCircle(w / 2f, h / 2f, (1f - t) * maxR);
        canvas.ClipPath(path);
        canvas.DrawBitmap(from, 0, 0);
        canvas.Restore();
    }

    // ── Venetian blinds ───────────────────────────────────────────────────────

    private const int VenetianStripH = 16;

    private static void RenderVenetianBlinds(SKBitmap from, SKBitmap to, SKBitmap result, float t)
    {
        int w = result.Width, h = result.Height;
        using var canvas = new SKCanvas(result);
        canvas.DrawBitmap(from, 0, 0);

        int strips = (h + VenetianStripH - 1) / VenetianStripH;
        int revealH = (int)(t * VenetianStripH);
        if (revealH <= 0) return;

        for (int k = 0; k < strips; k++)
        {
            int y0 = k * VenetianStripH;
            int y1 = Math.Min(y0 + VenetianStripH, h);
            int reveal = Math.Min(revealH, y1 - y0);
            canvas.Save();
            canvas.ClipRect(SKRect.Create(0, y0, w, reveal));
            canvas.DrawBitmap(to, 0, 0);
            canvas.Restore();
        }
    }

    // ── Barn door ─────────────────────────────────────────────────────────────

    private static void RenderBarnDoor(SKBitmap from, SKBitmap to, SKBitmap result, float t, bool open)
    {
        int w = result.Width, h = result.Height;
        using var canvas = new SKCanvas(result);

        if (open)
        {
            // "from" (old card) is the two doors; "to" (new card) is revealed behind.
            canvas.DrawBitmap(to, 0, 0);
            float offset = t * (w / 2f);
            // Left door slides left
            canvas.Save();
            canvas.ClipRect(SKRect.Create(0, 0, w / 2f, h));
            canvas.DrawBitmap(from, -offset, 0);
            canvas.Restore();
            // Right door slides right
            canvas.Save();
            canvas.ClipRect(SKRect.Create(w / 2f, 0, w / 2f, h));
            canvas.DrawBitmap(from, offset, 0);
            canvas.Restore();
        }
        else
        {
            // "to" (new card) halves close in from the edges over "from".
            canvas.DrawBitmap(from, 0, 0);
            float offset = (1f - t) * (w / 2f);
            // Left half of to comes from the left
            canvas.Save();
            canvas.ClipRect(SKRect.Create(0, 0, w / 2f, h));
            canvas.DrawBitmap(to, -offset, 0);
            canvas.Restore();
            // Right half of to comes from the right
            canvas.Save();
            canvas.ClipRect(SKRect.Create(w / 2f, 0, w / 2f, h));
            canvas.DrawBitmap(to, offset, 0);
            canvas.Restore();
        }
    }

    // ── Checkerboard ──────────────────────────────────────────────────────────

    private const int CheckerCellSize = 16;

    private static void RenderCheckerboard(SKBitmap from, SKBitmap to, SKBitmap result, float t)
    {
        int w = result.Width, h = result.Height;
        using var canvas = new SKCanvas(result);
        canvas.DrawBitmap(from, 0, 0);

        // Group 0 (even cells) reveal during t ∈ [0, 0.5]; group 1 during [0.5, 1].
        int colCells = (w + CheckerCellSize - 1) / CheckerCellSize;
        int rowCells = (h + CheckerCellSize - 1) / CheckerCellSize;

        for (int ry = 0; ry < rowCells; ry++)
        {
            for (int rx = 0; rx < colCells; rx++)
            {
                int group   = (rx + ry) % 2;
                float localT = group == 0
                    ? Math.Min(1f, t * 2f)
                    : Math.Max(0f, t * 2f - 1f);
                if (localT <= 0f) continue;

                int x0 = rx * CheckerCellSize;
                int y0 = ry * CheckerCellSize;
                int cw  = Math.Min(CheckerCellSize, w - x0);
                int ch  = Math.Min(CheckerCellSize, h - y0);
                int revealH = (int)(localT * ch);
                if (revealH <= 0) continue;

                canvas.Save();
                canvas.ClipRect(SKRect.Create(x0, y0, cw, revealH));
                canvas.DrawBitmap(to, 0, 0);
                canvas.Restore();
            }
        }
    }

    // ── Stretch / shrink ──────────────────────────────────────────────────────

    private static void RenderStretchFromCenter(SKBitmap from, SKBitmap to, SKBitmap result, float t)
    {
        int w = result.Width, h = result.Height;
        using var canvas = new SKCanvas(result);
        canvas.DrawBitmap(from, 0, 0);
        float scaledH = h * t;
        float y0 = (h - scaledH) / 2f;
        canvas.DrawBitmap(to, SKRect.Create(0, y0, w, scaledH));
    }

    private static void RenderStretchFromTop(SKBitmap from, SKBitmap to, SKBitmap result, float t)
    {
        int w = result.Width, h = result.Height;
        using var canvas = new SKCanvas(result);
        canvas.DrawBitmap(from, 0, 0);
        canvas.DrawBitmap(to, SKRect.Create(0, 0, w, h * t));
    }

    private static void RenderStretchFromBottom(SKBitmap from, SKBitmap to, SKBitmap result, float t)
    {
        int w = result.Width, h = result.Height;
        using var canvas = new SKCanvas(result);
        canvas.DrawBitmap(from, 0, 0);
        float scaledH = h * t;
        canvas.DrawBitmap(to, SKRect.Create(0, h - scaledH, w, scaledH));
    }

    private static void RenderShrinkToCenter(SKBitmap from, SKBitmap to, SKBitmap result, float t)
    {
        int w = result.Width, h = result.Height;
        using var canvas = new SKCanvas(result);
        canvas.DrawBitmap(to, 0, 0);
        float scaledH = h * (1f - t);
        float y0 = (h - scaledH) / 2f;
        canvas.DrawBitmap(from, SKRect.Create(0, y0, w, scaledH));
    }

    private static void RenderShrinkToTop(SKBitmap from, SKBitmap to, SKBitmap result, float t)
    {
        int w = result.Width, h = result.Height;
        using var canvas = new SKCanvas(result);
        canvas.DrawBitmap(to, 0, 0);
        canvas.DrawBitmap(from, SKRect.Create(0, 0, w, h * (1f - t)));
    }

    private static void RenderShrinkToBottom(SKBitmap from, SKBitmap to, SKBitmap result, float t)
    {
        int w = result.Width, h = result.Height;
        using var canvas = new SKCanvas(result);
        canvas.DrawBitmap(to, 0, 0);
        float scaledH = h * (1f - t);
        canvas.DrawBitmap(from, SKRect.Create(0, h - scaledH, w, scaledH));
    }

    // ── Dissolve ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Pixel-level dissolve using a Fisher-Yates-shuffled reveal order.
    /// The rank array is computed once per (width × height) and reused.
    /// </summary>
    private static unsafe void RenderDissolve(SKBitmap from, SKBitmap to, SKBitmap result, float t)
    {
        int w = result.Width, h = result.Height;
        int n = w * h;
        int threshold = (int)(t * n);

        int[] rank = GetDissolveRank(w, h);

        uint* pFrom    = (uint*)from.GetPixels().ToPointer();
        uint* pTo      = (uint*)to.GetPixels().ToPointer();
        uint* pResult  = (uint*)result.GetPixels().ToPointer();

        for (int p = 0; p < n; p++)
            pResult[p] = rank[p] < threshold ? pTo[p] : pFrom[p];
    }

    private static int[] GetDissolveRank(int w, int h)
    {
        lock (_dissolveLock)
        {
            if (_dissolveCache is { W: var cw, H: var ch, Rank: var cr }
                && cw == w && ch == h)
                return cr;

            int n = w * h;
            // Build shuffled order with a fixed seed for reproducibility.
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            var rng = new Random(0x1337CAFE);
            for (int i = n - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            // Invert: rank[p] = position of pixel p in the reveal sequence.
            var rank = new int[n];
            for (int k = 0; k < n; k++)
                rank[order[k]] = k;

            _dissolveCache = (w, h, rank);
            return rank;
        }
    }
}
