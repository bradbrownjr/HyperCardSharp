using System.Buffers.Binary;

namespace HyperCardSharp.Core.Containers;

/// <summary>
/// Extracts the raw HFS disk image from a DiskCopy 4.2 (.img) file.
/// Header is 84 bytes; raw disk data follows immediately.
/// </summary>
public class DiskCopyExtractor : IContainerExtractor
{
    // DiskCopy 4.2 header layout:
    // 0x00: diskName (64 bytes, Pascal string)
    // 0x40: dataSize (4, BE)
    // 0x44: tagSize (4, BE)
    // 0x48: dataChecksum (4)
    // 0x4C: tagChecksum (4)
    // 0x50: diskFormat (1)
    // 0x51: formatByte (1)
    // 0x52: privateWord (2, should be 0x0100)
    private const int HeaderSize = 84;

    public bool CanHandle(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            return false;

        // privateWord at offset 0x52 should be 0x0100
        if (data[0x52] != 0x01 || data[0x53] != 0x00)
            return false;

        // Disk name length must be 1-63
        int nameLen = data[0];
        if (nameLen < 1 || nameLen > 63)
            return false;

        // dataSize must be positive and fit within file
        int dataSize = BinaryPrimitives.ReadInt32BigEndian(data.Slice(0x40, 4));
        if (dataSize <= 0 || dataSize > data.Length - HeaderSize)
            return false;

        return true;
    }

    public byte[]? Extract(byte[] data)
    {
        if (!CanHandle(data))
            return null;

        try
        {
            var span = data.AsSpan();
            int dataSize = BinaryPrimitives.ReadInt32BigEndian(span.Slice(0x40, 4));

            if (HeaderSize + dataSize > span.Length)
                return null;

            // Raw disk data starts at offset 84
            return span.Slice(HeaderSize, dataSize).ToArray();
        }
        catch
        {
            return null;
        }
    }
}
