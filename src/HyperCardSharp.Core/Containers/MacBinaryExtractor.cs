using System.Buffers.Binary;
using System.Text;

namespace HyperCardSharp.Core.Containers;

/// <summary>
/// Extracts data from MacBinary I/II wrapped files.
/// MacBinary header is 128 bytes; data fork follows, then resource fork (both padded to 128-byte boundaries).
/// </summary>
public class MacBinaryExtractor : IContainerExtractor
{
    public bool CanHandle(ReadOnlySpan<byte> data)
    {
        if (data.Length < 128)
            return false;

        // byte 0: always 0x00 (old version)
        if (data[0] != 0x00)
            return false;

        // byte 1: filename length (1-63)
        int nameLen = data[1];
        if (nameLen < 1 || nameLen > 63)
            return false;

        // Validate filename bytes are printable ASCII
        for (int i = 2; i < 2 + nameLen; i++)
        {
            if (data[i] < 0x20 || data[i] > 0x7E)
                return false;
        }

        // byte 74: always 0x00
        if (data[74] != 0x00)
            return false;

        // byte 82: always 0x00 (protected bit check in spec, but we check 83 per spec)
        if (data[82] != 0x00)
            return false;

        // Data fork length must be reasonable
        int dataForkLen = BinaryPrimitives.ReadInt32BigEndian(data.Slice(84, 4));
        if (dataForkLen < 0 || dataForkLen > data.Length - 128)
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

            // File type at bytes 65-68
            string fileType = Encoding.ASCII.GetString(span.Slice(65, 4));

            int dataForkLen = BinaryPrimitives.ReadInt32BigEndian(span.Slice(84, 4));
            int rsrcForkLen = BinaryPrimitives.ReadInt32BigEndian(span.Slice(88, 4));

            // Data fork starts at offset 128
            int dataForkOffset = 128;

            // Resource fork follows data fork, padded to 128-byte boundary
            int paddedDataLen = dataForkLen == 0 ? 0 : ((dataForkLen + 127) / 128) * 128;
            int rsrcForkOffset = dataForkOffset + paddedDataLen;

            // If type is "STAK", return data fork
            if (fileType == "STAK" && dataForkLen > 0)
            {
                return data.AsSpan(dataForkOffset, dataForkLen).ToArray();
            }

            // Check if resource fork contains STAK magic (at offset 4 within resource fork)
            if (rsrcForkLen > 8 && rsrcForkOffset + rsrcForkLen <= data.Length)
            {
                var rsrc = span.Slice(rsrcForkOffset, rsrcForkLen);
                if (rsrc.Length >= 8 &&
                    rsrc[4] == 'S' && rsrc[5] == 'T' && rsrc[6] == 'A' && rsrc[7] == 'K')
                {
                    return rsrc.ToArray();
                }
            }

            // Return data fork as fallback
            if (dataForkLen > 0 && dataForkOffset + dataForkLen <= data.Length)
                return data.AsSpan(dataForkOffset, dataForkLen).ToArray();

            return null;
        }
        catch
        {
            return null;
        }
    }
}
