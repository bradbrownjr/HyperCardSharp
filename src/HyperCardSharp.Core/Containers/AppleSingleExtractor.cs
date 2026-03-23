using System.Buffers.Binary;

namespace HyperCardSharp.Core.Containers;

/// <summary>
/// Extracts data from AppleSingle and AppleDouble wrapper files.
/// AppleSingle magic = 0x00051600, AppleDouble magic = 0x00051607.
/// </summary>
public class AppleSingleExtractor : IContainerExtractor
{
    private const uint AppleSingleMagic = 0x00051600;
    private const uint AppleDoubleMagic = 0x00051607;

    // Entry IDs
    private const uint EntryDataFork = 1;
    private const uint EntryResourceFork = 2;

    public bool CanHandle(ReadOnlySpan<byte> data)
    {
        if (data.Length < 26)
            return false;

        uint magic = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(0, 4));
        return magic == AppleSingleMagic || magic == AppleDoubleMagic;
    }

    public byte[]? Extract(byte[] data)
    {
        if (!CanHandle(data))
            return null;

        try
        {
            var span = data.AsSpan();

            // Header: magic(4) + version(4) + homeFS(16) + numEntries(2) = 26 bytes
            ushort numEntries = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(24, 2));

            if (numEntries == 0 || numEntries > 1000)
                return null;

            int entryBase = 26;
            int dataForkOffset = -1, dataForkLen = 0;
            int rsrcForkOffset = -1, rsrcForkLen = 0;

            for (int i = 0; i < numEntries; i++)
            {
                int descOffset = entryBase + i * 12;
                if (descOffset + 12 > span.Length)
                    break;

                uint entryId = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(descOffset, 4));
                int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(span.Slice(descOffset + 4, 4));
                int length = (int)BinaryPrimitives.ReadUInt32BigEndian(span.Slice(descOffset + 8, 4));

                if (entryId == EntryDataFork)
                {
                    dataForkOffset = offset;
                    dataForkLen = length;
                }
                else if (entryId == EntryResourceFork)
                {
                    rsrcForkOffset = offset;
                    rsrcForkLen = length;
                }
            }

            // Return data fork if it looks like STAK
            if (dataForkOffset >= 0 && dataForkLen >= 8 && dataForkOffset + dataForkLen <= span.Length)
            {
                var df = span.Slice(dataForkOffset, dataForkLen);
                if (df[4] == 'S' && df[5] == 'T' && df[6] == 'A' && df[7] == 'K')
                    return df.ToArray();
            }

            // Return resource fork if it looks like STAK
            if (rsrcForkOffset >= 0 && rsrcForkLen >= 8 && rsrcForkOffset + rsrcForkLen <= span.Length)
            {
                var rf = span.Slice(rsrcForkOffset, rsrcForkLen);
                if (rf.Length >= 8 && rf[4] == 'S' && rf[5] == 'T' && rf[6] == 'A' && rf[7] == 'K')
                    return rf.ToArray();
            }

            // Return data fork as fallback
            if (dataForkOffset >= 0 && dataForkLen > 0 && dataForkOffset + dataForkLen <= span.Length)
                return span.Slice(dataForkOffset, dataForkLen).ToArray();

            return null;
        }
        catch
        {
            return null;
        }
    }
}
