namespace HyperCardSharp.Core.Bitmap;

/// <summary>
/// Decoded 1-bit-per-pixel bitmap at card dimensions.
/// Bit 1 = black, bit 0 = white/transparent. MSB = leftmost pixel.
/// </summary>
public class BitmapImage
{
    public int Width { get; init; }
    public int Height { get; init; }
    public int RowBytes { get; init; }
    public byte[] Data { get; init; } = Array.Empty<byte>();

    /// <summary>
    /// Get pixel value at (x, y). Returns true if black, false if white.
    /// </summary>
    public bool GetPixel(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return false;
        int byteIndex = y * RowBytes + x / 8;
        int bitIndex = 7 - (x % 8); // MSB = leftmost
        return (Data[byteIndex] & (1 << bitIndex)) != 0;
    }
}
