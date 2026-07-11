using HyperCardSharp.Core.Bitmap;
using SkiaSharp;

namespace HyperCardSharp.Rendering;

/// <summary>
/// Converts decoded 1-bit BitmapImage to SkiaSharp SKBitmap.
/// </summary>
public static class BitmapRenderer
{
    /// <summary>
    /// Convert a decoded WOBA BitmapImage to a 32-bit BGRA SKBitmap using true
    /// three-state compositing: Data bit=1 -> opaque black; Data bit=0 with
    /// Mask bit=1 -> opaque white; Data bit=0 with Mask bit=0 -> transparent
    /// (lets whatever is drawn underneath — background art, an AddColor fill —
    /// show through). Used for both the background and card bitmap layers;
    /// drawing a transparent pixel over a white canvas looks identical to
    /// drawing an opaque white one, so one conversion serves both layers.
    /// </summary>
    public static SKBitmap ToSKBitmap(BitmapImage image)
    {
        var bitmap = new SKBitmap(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var pixels = new uint[image.Width * image.Height];

        const uint black = 0xFF000000;
        const uint white = 0xFFFFFFFF;
        const uint transparent = 0x00000000;

        // A BitmapImage without a mask (e.g. a hand-built test fixture) falls
        // back to fully opaque white for Data bit=0, matching the old
        // no-transparency behavior.
        bool hasMask = image.Mask.Length == image.Data.Length && image.Data.Length > 0;

        for (int y = 0; y < image.Height; y++)
        {
            int rowStart = y * image.RowBytes;
            int pixelRow = y * image.Width;

            for (int x = 0; x < image.Width; x++)
            {
                int byteIndex = rowStart + x / 8;
                int bitIndex = 7 - (x % 8);
                bool isInk = (image.Data[byteIndex] & (1 << bitIndex)) != 0;

                if (isInk)
                {
                    pixels[pixelRow + x] = black;
                }
                else if (!hasMask || (image.Mask[byteIndex] & (1 << bitIndex)) != 0)
                {
                    pixels[pixelRow + x] = white;
                }
                else
                {
                    pixels[pixelRow + x] = transparent;
                }
            }
        }

        unsafe
        {
            fixed (uint* ptr = pixels)
            {
                var span = new ReadOnlySpan<byte>(ptr, pixels.Length * 4);
                bitmap.Bytes.AsSpan().Clear();
                span.CopyTo(bitmap.GetPixelSpan());
            }
        }

        return bitmap;
    }
}
