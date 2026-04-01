using HyperCardSharp.Core.Bitmap;
using HyperCardSharp.Core.Stack;
using SkiaSharp;

namespace HyperCardSharp.Rendering;

/// <summary>
/// Renders a complete HyperCard card by compositing background and card bitmaps,
/// then overlaying field text content from PartContent records.
/// </summary>
public class CardRenderer
{
    private readonly StackFile _stack;
    private readonly Dictionary<int, SKBitmap> _bitmapCache = new();

    public CardRenderer(StackFile stack)
    {
        _stack = stack;
    }

    /// <summary>
    /// Render a card to an SKBitmap at stack dimensions.
    /// </summary>
    public SKBitmap RenderCard(CardBlock card, RenderMode mode = RenderMode.BlackAndWhite)
    {
        int width = _stack.StackHeader.CardWidth;
        int height = _stack.StackHeader.CardHeight;

        // Guard against stacks with zero or negative dimensions
        if (width <= 0) width = 512;
        if (height <= 0) height = 342;

        var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888));
        if (surface == null)
        {
            // Fallback: return a blank bitmap
            var fallback = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var c = new SKCanvas(fallback);
            c.Clear(SKColors.White);
            return fallback;
        }
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        // Draw background bitmap (if any)
        var bg = _stack.Backgrounds.FirstOrDefault(b => b.Header.Id == card.BackgroundId);
        if (bg != null && bg.BitmapId != 0)
        {
            var bgBitmap = GetOrDecodeBitmap(bg.BitmapId, out _);
            if (bgBitmap != null)
                canvas.DrawBitmap(bgBitmap, 0, 0);
        }

        // Draw card bitmap on top
        (int Left, int Top, int Right, int Bottom)? cardImgRect = null;
        if (card.BitmapId != 0)
        {
            var cardBitmap = GetOrDecodeBitmap(card.BitmapId, out var cardBmap);
            if (cardBitmap != null)
                canvas.DrawBitmap(cardBitmap, 0, 0);
            if (cardBmap != null)
                cardImgRect = (cardBmap.ImageRect.Left, cardBmap.ImageRect.Top,
                               cardBmap.ImageRect.Right, cardBmap.ImageRect.Bottom);
        }

        // Overlay field text and non-Transparent button chrome
        if (bg != null)
            PartRenderer.RenderBackgroundParts(canvas, bg, card, cardImgRect);
        PartRenderer.RenderCardParts(canvas, card, cardImgRect);

        // TODO: Phase 8 Color — apply AddColor overlays when mode == RenderMode.Color
        // and AddColor resource data is available for this card/background.

        // Extract the rendered image
        using var image = surface.Snapshot();
        var result = SKBitmap.FromImage(image);
        surface.Dispose();
        return result;
    }

    private SKBitmap? GetOrDecodeBitmap(int bmapId, out BitmapBlock? bmap)
    {
        bmap = null;
        if (_bitmapCache.TryGetValue(bmapId, out var cached))
        {
            _stack.Bitmaps.TryGetValue(bmapId, out bmap);
            return cached;
        }

        if (!_stack.Bitmaps.TryGetValue(bmapId, out bmap))
            return null;

        var blockHeader = _stack.Blocks.FirstOrDefault(b => b.Type == "BMAP" && b.Id == bmapId);
        if (blockHeader.Type == null)
            return null;

        var blockData = _stack.GetBlockData(blockHeader);
        var decoded = WobaDecoder.Decode(blockData, bmap);
        var skBitmap = BitmapRenderer.ToSKBitmap(decoded);

        _bitmapCache[bmapId] = skBitmap;
        return skBitmap;
    }

    public void ClearCache()
    {
        foreach (var bmp in _bitmapCache.Values)
            bmp.Dispose();
        _bitmapCache.Clear();
    }
}
