using HyperCardSharp.Core.Bitmap;
using SkiaSharp;

namespace HyperCardSharp.Rendering.Tests;

public class BitmapRendererTests
{
    /// <summary>
    /// Hand-built 8x1 BitmapImage (1 row, 1 byte) exercising all three WOBA
    /// pixel states in a single byte: bit 0 = ink (opaque black), bit 1 =
    /// opaque white (mask=1, image=0), bit 2 = transparent (mask=0, image=0).
    /// MSB is leftmost, so pixel x corresponds to bit (7-x).
    /// </summary>
    private static BitmapImage BuildThreeStateImage()
    {
        // bit7(x=0)=ink, bit6(x=1)=opaque-white, bit5(x=2)=transparent, rest=0
        byte dataByte = 0b1000_0000; // x=0 is ink
        byte maskByte = 0b1100_0000; // x=0 and x=1 are opaque; x=2 onward transparent

        return new BitmapImage
        {
            Width = 8,
            Height = 1,
            RowBytes = 1,
            Data = new[] { dataByte },
            Mask = new[] { maskByte }
        };
    }

    [Fact]
    public void ToSKBitmap_InkPixel_IsOpaqueBlack()
    {
        var image = BuildThreeStateImage();
        using var bmp = BitmapRenderer.ToSKBitmap(image);

        var pixel = bmp.GetPixel(0, 0);
        Assert.Equal(new SKColor(0, 0, 0, 255), pixel);
    }

    [Fact]
    public void ToSKBitmap_MaskedWhitePixel_IsOpaqueWhite()
    {
        var image = BuildThreeStateImage();
        using var bmp = BitmapRenderer.ToSKBitmap(image);

        var pixel = bmp.GetPixel(1, 0);
        Assert.Equal(new SKColor(255, 255, 255, 255), pixel);
    }

    [Fact]
    public void ToSKBitmap_UnmaskedPixel_IsFullyTransparent()
    {
        var image = BuildThreeStateImage();
        using var bmp = BitmapRenderer.ToSKBitmap(image);

        var pixel = bmp.GetPixel(2, 0);
        Assert.Equal(0, pixel.Alpha);
    }

    [Fact]
    public void ToSKBitmap_NoMaskProvided_FallsBackToOpaqueWhite()
    {
        // Backward-compat: a BitmapImage without a Mask array (e.g. a test
        // fixture built before this feature existed) must still render
        // Data bit=0 as opaque white, not transparent.
        var image = new BitmapImage
        {
            Width = 8,
            Height = 1,
            RowBytes = 1,
            Data = new byte[] { 0b1000_0000 } // only x=0 is ink; Mask left empty
        };

        using var bmp = BitmapRenderer.ToSKBitmap(image);

        Assert.Equal(new SKColor(0, 0, 0, 255), bmp.GetPixel(0, 0));
        Assert.Equal(new SKColor(255, 255, 255, 255), bmp.GetPixel(1, 0));
    }
}
