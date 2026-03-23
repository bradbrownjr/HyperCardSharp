using System.Buffers.Binary;
using System.Text;

namespace HyperCardSharp.Core.Containers;

/// <summary>
/// Last-resort extractor that scans raw data (e.g., unrecognized disk images)
/// for contiguous HyperCard stack blocks. Finds the STAK block header,
/// then walks consecutive blocks (MAST, LIST, PAGE, CARD, BKGD, BMAP, etc.)
/// and returns the largest valid stack found.
/// </summary>
public class RawStackScanner : IContainerExtractor
{
    // Minimum file size to bother scanning (must be larger than a raw stack would be alone)
    private const int MinScanSize = 100_000;

    // Known HyperCard block type codes (4-char ASCII)
    private static readonly HashSet<string> KnownBlockTypes = new(StringComparer.Ordinal)
    {
        "STAK", "MAST", "LIST", "PAGE", "BKGD", "CARD", "BMAP",
        "FREE", "STBL", "FTBL", "PRNT", "PRST", "PRFT", "TAIL", "SND ", "MARK"
    };

    public bool CanHandle(ReadOnlySpan<byte> data)
    {
        // Only activate as a fallback for large files that might be disk images
        // and that aren't already a raw stack (STAK at offset 4)
        if (data.Length < MinScanSize)
            return false;

        // If it's already a raw stack, skip scanning
        if (data.Length >= 8 &&
            data[4] == 'S' && data[5] == 'T' && data[6] == 'A' && data[7] == 'K')
            return false;

        // Quick scan: does the file contain "STAK" anywhere?
        // Check every 512 bytes (sector boundary) for efficiency
        for (int i = 4; i < data.Length - 4; i += 512)
        {
            if (data[i] == 'S' && data[i + 1] == 'T' && data[i + 2] == 'A' && data[i + 3] == 'K')
                return true;
        }

        return false;
    }

    public byte[]? Extract(byte[] data)
    {
        var all = ExtractAll(data);
        if (all.Count == 0) return null;

        // Return the largest stack
        (string _, byte[] best) = all[0];
        for (int i = 1; i < all.Count; i++)
            if (all[i].Data.Length > best.Length)
                best = all[i].Data;
        return best;
    }

    /// <summary>
    /// Extract all valid stacks found in the raw data.
    /// Each result is (name, data) where name is derived from the stack's card count.
    /// </summary>
    public List<(string Name, byte[] Data)> ExtractAll(byte[] data)
    {
        var results = new List<(string Name, byte[] Data)>();
        var span = data.AsSpan();

        for (int i = 0; i <= data.Length - 8; i++)
        {
            if (data[i + 4] != 'S' || data[i + 5] != 'T' ||
                data[i + 6] != 'A' || data[i + 7] != 'K')
                continue;

            int blockSize = BinaryPrimitives.ReadInt32BigEndian(span.Slice(i, 4));
            if (blockSize < 256 || blockSize > 100_000 || i + blockSize > data.Length)
                continue;

            if (i + 20 > data.Length) continue;
            int version = BinaryPrimitives.ReadInt32BigEndian(span.Slice(i + 16, 4));
            if (version < 1 || version > 20)
                continue;

            int totalLength = WalkBlocks(data, i);
            if (totalLength < 1024)
                continue;

            var stackData = new byte[totalLength];
            Array.Copy(data, i, stackData, 0, totalLength);

            // Build a descriptive name from the STAK block header
            string name = BuildStackName(stackData, totalLength);
            results.Add((name, stackData));
        }

        return results;
    }

    private static string BuildStackName(byte[] stackData, int totalLength)
    {
        var span = stackData.AsSpan();
        int stkBlockSize = BinaryPrimitives.ReadInt32BigEndian(span.Slice(0, 4));

        // Try to extract the stack name from the STAK block's script.
        // The script is null-terminated and often starts with "-- stackName" or
        // contains "on openStack" referencing the stack.
        // More reliably: the STAK block's name field sits after parts/contents,
        // but it's at a variable offset. We'll try reading it from the script area.
        string stackName = TryExtractStackName(stackData, stkBlockSize);

        // Count CARD blocks
        int cardCount = 0;
        int pos = 0;
        while (pos + 8 <= stackData.Length)
        {
            int sz = BinaryPrimitives.ReadInt32BigEndian(span.Slice(pos, 4));
            if (sz < 16 || pos + sz > stackData.Length) break;
            if (stackData[pos + 4] == 'C' && stackData[pos + 5] == 'A' &&
                stackData[pos + 6] == 'R' && stackData[pos + 7] == 'D')
                cardCount++;
            pos += sz;
        }

        // Read card dimensions from STAK block
        string dims = "";
        if (stkBlockSize >= 0x1BC && stackData.Length >= 0x1BC)
        {
            short h = BinaryPrimitives.ReadInt16BigEndian(span.Slice(0x1B8, 2));
            short w = BinaryPrimitives.ReadInt16BigEndian(span.Slice(0x1BA, 2));
            if (w > 0 && h > 0)
                dims = $", {w}x{h}";
        }

        string label = $"{cardCount} cards{dims}, {totalLength / 1024}K";
        return stackName != null
            ? $"{stackName} ({label})"
            : $"Stack ({label})";
    }

    /// <summary>
    /// Try to find a name by reading the first CARD block's name field.
    /// CARD layout: +0x28=partCount(2), +0x2C=partListSize(4),
    ///   +0x30=partContentCount(2), +0x32=partContentSize(4),
    ///   name starts at +0x36 + partListSize + partContentSize (null-terminated).
    /// Falls back to first BKGD block name with same layout.
    /// </summary>
    private static string? TryExtractStackName(byte[] stackData, int stkBlockSize)
    {
        var span = stackData.AsSpan();
        // Walk blocks looking for first CARD or BKGD
        string? bgName = null;
        int pos = 0;
        while (pos + 8 <= stackData.Length)
        {
            int blockSize = BinaryPrimitives.ReadInt32BigEndian(span.Slice(pos, 4));
            if (blockSize < 16 || pos + blockSize > stackData.Length) break;

            bool isCard = stackData[pos+4]=='C' && stackData[pos+5]=='A' && stackData[pos+6]=='R' && stackData[pos+7]=='D';
            bool isBkgd = stackData[pos+4]=='B' && stackData[pos+5]=='K' && stackData[pos+6]=='G' && stackData[pos+7]=='D';

            if ((isCard || isBkgd) && blockSize >= 0x36)
            {
                string? name = TryReadBlockName(stackData, pos, blockSize);
                if (!string.IsNullOrEmpty(name))
                {
                    if (isCard) return name; // First named card wins
                    bgName ??= name;         // Keep first bg name as fallback
                }
            }
            pos += blockSize;
        }
        return bgName;
    }

    private static string? TryReadBlockName(byte[] data, int blockStart, int blockSize)
    {
        int offset = blockStart + 0x2C;
        if (offset + 10 > data.Length) return null;

        var span = data.AsSpan();
        int partListSize = BinaryPrimitives.ReadInt32BigEndian(span.Slice(offset, 4));
        int partContentSize = BinaryPrimitives.ReadInt32BigEndian(span.Slice(offset + 6, 4));

        // Name starts at block + 0x36 + partListSize + partContentSize
        int nameOffset = blockStart + 0x36 + partListSize + partContentSize;
        if (nameOffset < blockStart || nameOffset >= blockStart + blockSize)
            return null;

        // Read null-terminated ASCII name
        int end = nameOffset;
        int limit = Math.Min(blockStart + blockSize, data.Length);
        while (end < limit && data[end] != 0)
            end++;

        int len = end - nameOffset;
        if (len < 1 || len > 255) return null;

        string name = Encoding.ASCII.GetString(data, nameOffset, len);
        // Validate it's actually readable text
        foreach (char c in name)
            if (c < 0x20 || c > 0x7E) return null;

        return name;
    }

    private static int WalkBlocks(byte[] data, int startOffset)
    {
        int pos = startOffset;
        var span = data.AsSpan();

        while (pos + 8 <= data.Length)
        {
            int blockSize = BinaryPrimitives.ReadInt32BigEndian(span.Slice(pos, 4));
            if (blockSize < 16 || blockSize > 10_000_000 || pos + blockSize > data.Length)
                break;

            // Read block type as ASCII
            bool validAscii = true;
            var typeChars = new char[4];
            for (int c = 0; c < 4; c++)
            {
                byte b = data[pos + 4 + c];
                if (b < 32 || b > 126) { validAscii = false; break; }
                typeChars[c] = (char)b;
            }
            if (!validAscii)
                break;

            string blockType = new string(typeChars);
            if (!KnownBlockTypes.Contains(blockType))
                break;

            pos += blockSize;
        }

        return pos - startOffset;
    }
}
