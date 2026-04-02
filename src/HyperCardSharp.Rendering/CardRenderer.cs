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
    private readonly Dictionary<int, SKBitmap> _bitmapCache      = new();
    private readonly Dictionary<int, SKBitmap> _bitmapCacheAlpha = new();
    private readonly Dictionary<short, SKBitmap> _iconCache       = new();

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

        bool isColor = mode == RenderMode.Color;

        // Draw background bitmap (if any)
        var bg = _stack.Backgrounds.FirstOrDefault(b => b.Header.Id == card.BackgroundId);
        if (bg != null)
        {
            // In Color mode: draw AddColor background fills before the bitmap so that
            // white areas of the B&W artwork reveal the colour underneath.
            if (isColor && _stack.BackgroundColorData.TryGetValue(bg.Header.Id, out var bgRegions))
                ColorRenderer.DrawColorRegions(canvas, bgRegions, bg.Parts);

            if (bg.BitmapId != 0)
            {
                var bgBmp = GetOrDecodeBitmap(bg.BitmapId, isColor, out _);
                if (bgBmp != null)
                    canvas.DrawBitmap(bgBmp, 0, 0);
            }
        }

        // Draw card bitmap on top
        (int Left, int Top, int Right, int Bottom)? cardImgRect = null;
        if (card.BitmapId != 0)
        {
            // In Color mode: draw AddColor card fills before the card bitmap
            if (isColor && _stack.CardColorData.TryGetValue(card.Header.Id, out var cardRegions))
                ColorRenderer.DrawColorRegions(canvas, cardRegions, card.Parts);

            var cardBitmap = GetOrDecodeBitmap(card.BitmapId, isColor, out var cardBmap);
            if (cardBitmap != null)
                canvas.DrawBitmap(cardBitmap, 0, 0);
            if (cardBmap != null)
                cardImgRect = (cardBmap.ImageRect.Left, cardBmap.ImageRect.Top,
                               cardBmap.ImageRect.Right, cardBmap.ImageRect.Bottom);
        }

        // Overlay field text and non-Transparent button chrome
        var icons = GetIconCache();
        if (bg != null)
            PartRenderer.RenderBackgroundParts(canvas, bg, card, icons, cardImgRect,
                _stack.StyleTable, _stack.FontTable);
        PartRenderer.RenderCardParts(canvas, card, icons, cardImgRect,
            _stack.StyleTable, _stack.FontTable);

        // Extract the rendered image
        using var image = surface.Snapshot();
        var result = SKBitmap.FromImage(image);
        surface.Dispose();
        return result;
    }

    private SKBitmap? GetOrDecodeBitmap(int bmapId, bool transparent, out BitmapBlock? bmap)
    {
        bmap = null;
        var cache = transparent ? _bitmapCacheAlpha : _bitmapCache;

        if (cache.TryGetValue(bmapId, out var cached))
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
        var decoded   = WobaDecoder.Decode(blockData, bmap);
        var skBitmap  = transparent
            ? BitmapRenderer.ToSKBitmapWithTransparency(decoded)
            : BitmapRenderer.ToSKBitmap(decoded);

        cache[bmapId] = skBitmap;
        return skBitmap;
    }

    internal IReadOnlyDictionary<short, SKBitmap> GetIconCache()
    {
        // Populate any icons not yet decoded
        foreach (var (id, raw) in _stack.Icons)
        {
            if (_iconCache.ContainsKey(id))
                continue;

            var pixels = Core.Resources.MacResourceForkReader.DecodeIcon(raw);
            if (pixels == null)
                continue;

            var bmp = new SKBitmap(32, 32, SKColorType.Bgra8888, SKAlphaType.Premul);
            for (int row = 0; row < 32; row++)
                for (int col = 0; col < 32; col++)
                    // 1-bits = black ink; 0-bits = transparent so the white button background shows through
                    bmp.SetPixel(col, row, pixels[row * 32 + col] ? SKColors.Black : SKColors.Transparent);

            _iconCache[id] = bmp;
        }

        return _iconCache;
    }

    public void ClearCache()
    {
        foreach (var bmp in _bitmapCache.Values)
            bmp.Dispose();
        _bitmapCache.Clear();
        foreach (var bmp in _bitmapCacheAlpha.Values)
            bmp.Dispose();
        _bitmapCacheAlpha.Clear();
        foreach (var bmp in _iconCache.Values)
            bmp.Dispose();
        _iconCache.Clear();
    }
}
