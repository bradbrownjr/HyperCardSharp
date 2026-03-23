using HyperCardSharp.Core.Bitmap;
using HyperCardSharp.Core.Stack;
using SkiaSharp;

namespace HyperCardSharp.Rendering;

/// <summary>
/// Renders a complete HyperCard card by compositing background and card bitmaps.
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
    public SKBitmap RenderCard(CardBlock card)
    {
        int width = _stack.StackHeader.CardWidth;
        int height = _stack.StackHeader.CardHeight;

        var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        // Draw background bitmap (if any)
        var bg = _stack.Backgrounds.FirstOrDefault(b => b.Header.Id == card.BackgroundId);
        if (bg != null && bg.BitmapId != 0)
        {
            var bgBitmap = GetOrDecodeBitmap(bg.BitmapId);
            if (bgBitmap != null)
                canvas.DrawBitmap(bgBitmap, 0, 0);
        }

        // Draw card bitmap on top
        if (card.BitmapId != 0)
        {
            var cardBitmap = GetOrDecodeBitmap(card.BitmapId);
            if (cardBitmap != null)
                canvas.DrawBitmap(cardBitmap, 0, 0);
        }

        // Extract the rendered image
        using var image = surface.Snapshot();
        var result = SKBitmap.FromImage(image);
        surface.Dispose();
        return result;
    }

    private SKBitmap? GetOrDecodeBitmap(int bmapId)
    {
        if (_bitmapCache.TryGetValue(bmapId, out var cached))
            return cached;

        if (!_stack.Bitmaps.TryGetValue(bmapId, out var bmap))
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
