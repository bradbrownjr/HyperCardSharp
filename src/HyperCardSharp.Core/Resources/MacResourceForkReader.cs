using System.Buffers.Binary;
using System.Text;

namespace HyperCardSharp.Core.Resources;

/// <summary>
/// Parses the classic Mac OS resource fork format and extracts typed resources.
///
/// Resource fork layout:
///   Offset 0-3:  offset to resource data section
///   Offset 4-7:  offset to resource map
///   Offset 8-11: length of resource data
///   Offset 12-15: length of resource map
///   [resource data section]: each entry prefixed by 4-byte data length
///   [resource map]: type list + reference lists + name list
///
/// Reference: Inside Macintosh Vol I, Chapter 5.
/// </summary>
public static class MacResourceForkReader
{
    /// <summary>
    /// Extract all resources of the given four-character type from a resource fork.
    /// Returns a dictionary of resourceId → raw resource data bytes.
    /// Returns an empty dictionary if the fork is absent, malformed, or contains
    /// no resources of the requested type.
    /// </summary>
    public static Dictionary<short, byte[]> GetResources(byte[]? fork, string type)
    {
        var result = new Dictionary<short, byte[]>();
        if (fork == null || fork.Length < 16 || type.Length != 4)
            return result;

        try
        {
            var span = fork.AsSpan();

            uint dataOffset = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(0, 4));
            uint mapOffset  = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(4, 4));

            // Quick sanity check — valid resource forks always start data at >= 256
            if (dataOffset < 16 || dataOffset >= (uint)fork.Length)
                return result;
            if (mapOffset < 16 || mapOffset + 28 > (uint)fork.Length)
                return result;

            // Resource map layout (offsets from start of map):
            //  0-15: reserved copy of fork header
            //  16-19: nextMap handle (not used)
            //  20-21: file reference number (not used)
            //  22-23: resource attributes
            //  24-25: offset to type list  (from start of map)
            //  26-27: offset to name list  (from start of map)
            ushort typeListOffset = BinaryPrimitives.ReadUInt16BigEndian(span.Slice((int)mapOffset + 24, 2));

            int typeListStart = (int)mapOffset + typeListOffset;
            if (typeListStart + 2 > fork.Length)
                return result;

            // Type list: first 2 bytes = (number of types − 1)
            short numTypesMinusOne = BinaryPrimitives.ReadInt16BigEndian(span.Slice(typeListStart, 2));
            int numTypes = numTypesMinusOne + 1;
            if (numTypes <= 0 || numTypes > 4096)
                return result;

            // Build the requested type code as a 4-byte big-endian uint for fast comparison.
            uint targetTypeCode = (uint)(type[0] << 24 | type[1] << 16 | type[2] << 8 | type[3]);

            for (int t = 0; t < numTypes; t++)
            {
                // Each type list entry: type(4) + count-1(2) + refListOffset(2) = 8 bytes
                int typeEntryOffset = typeListStart + 2 + t * 8;
                if (typeEntryOffset + 8 > fork.Length)
                    break;

                uint entryType = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(typeEntryOffset, 4));
                if (entryType != targetTypeCode)
                    continue;

                short numRefsMinusOne = BinaryPrimitives.ReadInt16BigEndian(span.Slice(typeEntryOffset + 4, 2));
                ushort refListOffset  = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(typeEntryOffset + 6, 2));
                int numRefs = numRefsMinusOne + 1;

                // Reference list starts at typeListStart + refListOffset.
                // Each reference entry is 12 bytes:
                //   0-1:  resource ID (Int16)
                //   2-3:  name offset in name list (Int16; -1 = no name)
                //   4:    resource attributes (1 byte)
                //   5-7:  offset of resource data from data section start (3 bytes, big-endian)
                //   8-11: reserved handle
                int refListStart = typeListStart + refListOffset;

                for (int r = 0; r < numRefs; r++)
                {
                    int refOffset = refListStart + r * 12;
                    if (refOffset + 12 > fork.Length)
                        break;

                    short resourceId = BinaryPrimitives.ReadInt16BigEndian(span.Slice(refOffset, 2));

                    // 3-byte big-endian data offset (bytes 5-7 of ref entry, i.e. refOffset+5)
                    int resDataOffset = (span[refOffset + 5] << 16) |
                                        (span[refOffset + 6] << 8)  |
                                         span[refOffset + 7];

                    int resourceDataStart = (int)dataOffset + resDataOffset;
                    if (resourceDataStart + 4 > fork.Length)
                        continue;

                    // Each resource record in the data section is: 4-byte length + data
                    uint resDataLen = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(resourceDataStart, 4));
                    if (resDataLen > 1024 * 1024)  // sanity cap: 1 MiB
                        continue;

                    int contentStart = resourceDataStart + 4;
                    if (contentStart + (int)resDataLen > fork.Length)
                        continue;

                    var resData = new byte[resDataLen];
                    span.Slice(contentStart, (int)resDataLen).CopyTo(resData);
                    result[resourceId] = resData;
                }

                break;  // found the requested type; no need to keep scanning
            }
        }
        catch
        {
            // Return whatever was collected before the fault.
        }

        return result;
    }

    /// <summary>
    /// Extract all resources of the given four-character type, returning each one with
    /// its resource name (null if not set in the name list).
    /// Returns an empty list if the fork is absent, malformed, or has no matching resources.
    /// </summary>
    public static List<(short Id, string? Name, byte[] Data)> GetResourcesWithNames(byte[]? fork, string type)
    {
        var result = new List<(short, string?, byte[])>();
        if (fork == null || fork.Length < 16 || type.Length != 4)
            return result;

        try
        {
            var span = fork.AsSpan();

            uint dataOffset = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(0, 4));
            uint mapOffset  = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(4, 4));

            if (dataOffset < 16 || dataOffset >= (uint)fork.Length)
                return result;
            if (mapOffset < 16 || mapOffset + 28 > (uint)fork.Length)
                return result;

            ushort typeListOffset = BinaryPrimitives.ReadUInt16BigEndian(span.Slice((int)mapOffset + 24, 2));
            ushort nameListOffset = BinaryPrimitives.ReadUInt16BigEndian(span.Slice((int)mapOffset + 26, 2));

            int typeListStart = (int)mapOffset + typeListOffset;
            int nameListStart = (int)mapOffset + nameListOffset;
            if (typeListStart + 2 > fork.Length)
                return result;

            short numTypesMinusOne = BinaryPrimitives.ReadInt16BigEndian(span.Slice(typeListStart, 2));
            int numTypes = numTypesMinusOne + 1;
            if (numTypes <= 0 || numTypes > 4096)
                return result;

            uint targetTypeCode = (uint)(type[0] << 24 | type[1] << 16 | type[2] << 8 | type[3]);

            for (int t = 0; t < numTypes; t++)
            {
                int typeEntryOffset = typeListStart + 2 + t * 8;
                if (typeEntryOffset + 8 > fork.Length)
                    break;

                uint entryType = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(typeEntryOffset, 4));
                if (entryType != targetTypeCode)
                    continue;

                short numRefsMinusOne = BinaryPrimitives.ReadInt16BigEndian(span.Slice(typeEntryOffset + 4, 2));
                ushort refListOffset  = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(typeEntryOffset + 6, 2));
                int numRefs = numRefsMinusOne + 1;
                int refListStart = typeListStart + refListOffset;

                for (int r = 0; r < numRefs; r++)
                {
                    int refOffset = refListStart + r * 12;
                    if (refOffset + 12 > fork.Length)
                        break;

                    short resourceId    = BinaryPrimitives.ReadInt16BigEndian(span.Slice(refOffset, 2));
                    short nameListOff   = BinaryPrimitives.ReadInt16BigEndian(span.Slice(refOffset + 2, 2));

                    int resDataOffset = (span[refOffset + 5] << 16) |
                                        (span[refOffset + 6] << 8)  |
                                         span[refOffset + 7];

                    int resourceDataStart = (int)dataOffset + resDataOffset;
                    if (resourceDataStart + 4 > fork.Length)
                        continue;

                    uint resDataLen = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(resourceDataStart, 4));
                    if (resDataLen > 1024 * 1024)
                        continue;

                    int contentStart = resourceDataStart + 4;
                    if (contentStart + (int)resDataLen > fork.Length)
                        continue;

                    var resData = new byte[resDataLen];
                    span.Slice(contentStart, (int)resDataLen).CopyTo(resData);

                    // Read Pascal string name from name list (nameListOff is offset within name list)
                    string? resName = null;
                    if (nameListOff >= 0 && nameListStart + nameListOff + 1 <= fork.Length)
                    {
                        int nameAbsOff = nameListStart + nameListOff;
                        byte nameLen = span[nameAbsOff];
                        if (nameAbsOff + 1 + nameLen <= fork.Length)
                            resName = Encoding.Latin1.GetString(span.Slice(nameAbsOff + 1, nameLen));
                    }

                    result.Add((resourceId, resName, resData));
                }
                break;
            }
        }
        catch
        {
            // Return whatever was collected before the fault.
        }

        return result;
    }

    /// <summary>
    /// Decode a classic Mac ICON resource (128 bytes = 32×32 1-bit bitmap, MSB first)
    /// into a flat bool array of length 1024 (row-major, top-left origin).
    /// Returns null if the data is not exactly 128 bytes.
    /// </summary>
    public static bool[]? DecodeIcon(byte[] data)
    {
        if (data.Length < 128)
            return null;

        var pixels = new bool[32 * 32];
        for (int row = 0; row < 32; row++)
        {
            for (int col = 0; col < 32; col++)
            {
                int byteIndex = row * 4 + col / 8;
                int bitIndex  = 7 - (col % 8);
                pixels[row * 32 + col] = ((data[byteIndex] >> bitIndex) & 1) == 1;
            }
        }
        return pixels;
    }
}
