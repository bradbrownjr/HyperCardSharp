using HyperCardSharp.Core.Bitmap;
using SkiaSharp;

namespace HyperCardSharp.Rendering;

/// <summary>
/// Converts decoded 1-bit BitmapImage to SkiaSharp SKBitmap.
/// </summary>
public static class BitmapRenderer
{
    /// <summary>
    /// Convert a 1-bit BitmapImage to a 32-bit BGRA SKBitmap.
    /// Black pixels (bit=1) → black, white pixels (bit=0) → white.
    /// </summary>
    public static SKBitmap ToSKBitmap(BitmapImage image)
    {
        var bitmap = new SKBitmap(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var pixels = new uint[image.Width * image.Height];

        const uint black = 0xFF000000;
        const uint white = 0xFFFFFFFF;

        for (int y = 0; y < image.Height; y++)
        {
            int rowStart = y * image.RowBytes;
            int pixelRow = y * image.Width;

            for (int x = 0; x < image.Width; x++)
            {
                int byteIndex = rowStart + x / 8;
                int bitIndex = 7 - (x % 8);
                bool isBlack = (image.Data[byteIndex] & (1 << bitIndex)) != 0;
                pixels[pixelRow + x] = isBlack ? black : white;
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

    /// <summary>
    /// Convert a 1-bit BitmapImage to a 32-bit BGRA SKBitmap with transparency.
    /// Black pixels (bit=1) → black, white pixels (bit=0) → transparent.
    /// Used for overlaying card bitmap on background.
    /// </summary>
    public static SKBitmap ToSKBitmapWithTransparency(BitmapImage image)
    {
        var bitmap = new SKBitmap(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var pixels = new uint[image.Width * image.Height];

        const uint black = 0xFF000000;
        const uint transparent = 0x00000000;

        for (int y = 0; y < image.Height; y++)
        {
            int rowStart = y * image.RowBytes;
            int pixelRow = y * image.Width;

            for (int x = 0; x < image.Width; x++)
            {
                int byteIndex = rowStart + x / 8;
                int bitIndex = 7 - (x % 8);
                bool isBlack = (image.Data[byteIndex] & (1 << bitIndex)) != 0;
                pixels[pixelRow + x] = isBlack ? black : transparent;
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
