using System.Buffers.Binary;
using System.Text;

namespace HyperCardSharp.Core.Containers;

/// <summary>
/// Parses an HFS+ filesystem image and extracts files with type "STAK".
/// Supports volumes where the HFS+ volume header (signature 0x482B "H+")
/// is at offset 1024, or embedded at a partition offset within the disk image.
/// </summary>
public class HfsPlusReader
{
    private const ushort HfsPlusSignature = 0x482B; // "H+"
    private const int VolumeHeaderOffsetFromVolumeStart = 1024;

    private readonly byte[] _disk;
    private int _volumeOffset; // byte offset where the HFS+ volume starts (before the 1024-byte header)
    private int _blockSize;

    public HfsPlusReader(byte[] diskImage)
    {
        _disk = diskImage;
    }

    /// <summary>
    /// Returns true if this looks like an HFS+ volume.
    /// Searches for the 0x482B signature at offset 1024 from potential volume starts.
    /// </summary>
    public bool IsHfsPlus()
    {
        return TryFindVolumeHeader();
    }

    /// <summary>
    /// Enumerate all STAK files on the HFS+ volume, returning their names and data forks.
    /// </summary>
    public List<(string Name, byte[] Data)> EnumerateStacks()
    {
        var results = new List<(string Name, byte[] Data)>();

        try
        {
            if (!TryFindVolumeHeader())
                return results;

            int headerOffset = _volumeOffset + VolumeHeaderOffsetFromVolumeStart;
            var header = _disk.AsSpan(headerOffset);

            _blockSize = (int)BinaryPrimitives.ReadUInt32BigEndian(header.Slice(40, 4));
            if (_blockSize < 512 || _blockSize > 65536)
                return results;

            // Catalog file fork data starts at offset 112 from the volume header
            var catalogFork = header.Slice(112, 80);
            long catalogLogicalSize = (long)BinaryPrimitives.ReadUInt64BigEndian(catalogFork.Slice(0, 8));
            if (catalogLogicalSize <= 0)
                return results;

            // Read catalog file data from its extents
            var catalogData = ReadForkData(catalogFork, catalogLogicalSize);
            if (catalogData == null || catalogData.Length < 512)
                return results;

            // Parse B-tree header node (node 0) to get nodeSize and firstLeafNode
            int nodeSize = ParseBTreeHeader(catalogData, out int firstLeafNode);
            if (nodeSize <= 0 || firstLeafNode <= 0)
                return results;

            // Walk leaf nodes via fLink chain
            int nodeNum = firstLeafNode;
            int nodesVisited = 0;
            int maxNodes = (int)(catalogLogicalSize / nodeSize) + 1;

            while (nodeNum > 0 && nodesVisited < maxNodes)
            {
                nodesVisited++;
                long nodeOffset = (long)nodeNum * nodeSize;
                if (nodeOffset + nodeSize > catalogData.Length)
                    break;

                var node = catalogData.AsSpan((int)nodeOffset, nodeSize);

                // Node descriptor: fLink(4), bLink(4), kind(1), height(1), numRecords(2), reserved(2)
                byte kind = node[8];

                // 0xFF = leaf node in HFS+
                if (kind != 0xFF)
                {
                    int fLink = (int)BinaryPrimitives.ReadUInt32BigEndian(node.Slice(0, 4));
                    nodeNum = fLink;
                    continue;
                }

                ushort numRecords = BinaryPrimitives.ReadUInt16BigEndian(node.Slice(10, 2));
                ScanLeafNode(node, nodeSize, numRecords, results);

                // Follow forward link
                int nextNode = (int)BinaryPrimitives.ReadUInt32BigEndian(node.Slice(0, 4));
                nodeNum = nextNode;
            }
        }
        catch
        {
            // Return whatever we found so far
        }

        return results;
    }

    private bool TryFindVolumeHeader()
    {
        // Try offset 1024 first (raw HFS+ volume, volume starts at 0)
        if (TryValidateVolumeAt(0))
            return true;

        // Scan the entire disk for the HFS+ signature at VH position (volumeStart + 1024)
        // The signature 0x482B can appear at any sector-aligned offset + 1024
        int limit = _disk.Length - VolumeHeaderOffsetFromVolumeStart - 120;
        for (int headerPos = VolumeHeaderOffsetFromVolumeStart + 512;
             headerPos < limit;
             headerPos += 512)
        {
            if (headerPos + 2 > _disk.Length) break;
            // Quick check for 'H+' before expensive validation
            if (_disk[headerPos] == 0x48 && _disk[headerPos + 1] == 0x2B)
            {
                int volumeStart = headerPos - VolumeHeaderOffsetFromVolumeStart;
                if (volumeStart >= 0 && TryValidateVolumeAt(volumeStart))
                    return true;
            }
        }

        return false;
    }

    private bool TryValidateVolumeAt(int volumeStart)
    {
        int headerOffset = volumeStart + VolumeHeaderOffsetFromVolumeStart;
        if (headerOffset + 120 > _disk.Length)
            return false;

        var span = _disk.AsSpan(headerOffset);
        ushort sig = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(0, 2));
        if (sig != HfsPlusSignature)
            return false;

        ushort version = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(2, 2));
        if (version != 4 && version != 5)
            return false;

        int blockSize = (int)BinaryPrimitives.ReadUInt32BigEndian(span.Slice(40, 4));
        if (blockSize < 512 || blockSize > 65536)
            return false;

        _volumeOffset = volumeStart;
        _blockSize = blockSize;
        return true;
    }

    private int ParseBTreeHeader(byte[] catalogData, out int firstLeafNode)
    {
        firstLeafNode = -1;

        if (catalogData.Length < 256)
            return -1;

        var headerNode = catalogData.AsSpan();

        // Node descriptor: fLink(4), bLink(4), kind(1), height(1), numRecords(2), reserved(2) = 14 bytes
        byte kind = headerNode[8];
        // Header node kind = 1
        if (kind != 1)
            return -1;

        // Header record starts at offset 14 within node 0
        // treeDepth(2), rootNode(4), leafRecords(4), firstLeafNode(4), lastLeafNode(4),
        // nodeSize is at offset 14 + 32 = 46
        if (catalogData.Length < 48)
            return -1;

        firstLeafNode = (int)BinaryPrimitives.ReadUInt32BigEndian(headerNode.Slice(14 + 10, 4));
        int nodeSize = BinaryPrimitives.ReadUInt16BigEndian(headerNode.Slice(14 + 32, 2));

        if (nodeSize < 512 || nodeSize > 65536)
            return -1;

        return nodeSize;
    }

    private void ScanLeafNode(ReadOnlySpan<byte> node, int nodeSize, ushort numRecords, List<(string Name, byte[] Data)> results)
    {
        for (int r = 0; r < numRecords; r++)
        {
            // Record offset table at end of node: entry r is at node[nodeSize - 2*(r+1)]
            int tablePos = nodeSize - 2 * (r + 1);
            if (tablePos < 14)
                break;

            ushort recOffset = BinaryPrimitives.ReadUInt16BigEndian(node.Slice(tablePos, 2));
            if (recOffset < 14 || recOffset >= nodeSize - 2)
                continue;

            // Next record offset (for bounds checking)
            int nextTablePos = nodeSize - 2 * (r + 2);
            int recEnd = nodeSize - 2 * (numRecords + 1); // conservative end
            if (nextTablePos >= 14 && r + 1 < numRecords)
            {
                recEnd = BinaryPrimitives.ReadUInt16BigEndian(node.Slice(nextTablePos, 2));
            }

            var rec = node.Slice(recOffset);
            int available = nodeSize - 2 * (numRecords + 1) - recOffset;
            if (available < 8)
                continue;

            // Catalog key: keyLength(2, BE), parentID(4, BE), name: unicodeLength(2, BE) + chars
            ushort keyLength = BinaryPrimitives.ReadUInt16BigEndian(rec.Slice(0, 2));
            if (keyLength < 6 || recOffset + 2 + keyLength > nodeSize)
                continue;

            uint parentID = BinaryPrimitives.ReadUInt32BigEndian(rec.Slice(2, 4));
            ushort unicodeLength = BinaryPrimitives.ReadUInt16BigEndian(rec.Slice(6, 2));

            string fileName = "";
            if (unicodeLength > 0 && 8 + unicodeLength * 2 <= 2 + keyLength)
            {
                // Read UTF-16BE characters
                byte[] nameBytes = new byte[unicodeLength * 2];
                for (int i = 0; i < nameBytes.Length; i++)
                    nameBytes[i] = rec[8 + i];
                fileName = Encoding.BigEndianUnicode.GetString(nameBytes);
            }

            // Data record starts after the key, aligned to 2 bytes
            int dataStart = recOffset + 2 + keyLength;
            if ((dataStart & 1) != 0) dataStart++; // word align

            if (dataStart + 2 > nodeSize)
                continue;

            // Record type
            ushort recordType = BinaryPrimitives.ReadUInt16BigEndian(node.Slice(dataStart, 2));
            if (recordType != 0x0002) // kHFSPlusFileRecord
                continue;

            // File record layout after recordType(2):
            // flags(2), reserved1(4), fileID(4), createDate(4), contentModDate(4),
            // attributeModDate(4), accessDate(4), backupDate(4),
            // permissions(16), userInfo(16), finderInfo(16), textEncoding(4), reserved2(4)
            // dataFork(80), resourceFork(80)

            // Minimum file record size: 2 + 2 + 4 + 4 + 4*4 + 16 + 16 + 16 + 4 + 4 + 80 + 80 = 248
            if (dataStart + 248 > nodeSize)
                continue;

            // userInfo (Finder info) starts at dataStart + 48
            // userInfo offset: recordType(2) + flags(2) + reserved1(4) + fileID(4) +
            //   createDate(4) + contentModDate(4) + attributeModDate(4) + accessDate(4) + backupDate(4) +
            //   permissions(16) = 48
            int userInfoOffset = dataStart + 48;
            if (userInfoOffset + 4 > nodeSize)
                continue;

            // First 4 bytes of userInfo = file type (e.g., "STAK")
            string fileType = Encoding.ASCII.GetString(node.Slice(userInfoOffset, 4));
            if (fileType != "STAK")
                continue;

            // Data fork starts at dataStart + 88
            // 48 (to userInfo) + 16 (userInfo) + 16 (finderInfo) + 4 (textEncoding) + 4 (reserved2) = 88
            int dataForkOffset = dataStart + 88;
            if (dataForkOffset + 80 > nodeSize)
                continue;

            var dataFork = node.Slice(dataForkOffset, 80);
            long logicalSize = (long)BinaryPrimitives.ReadUInt64BigEndian(dataFork.Slice(0, 8));
            if (logicalSize <= 0 || logicalSize > int.MaxValue)
                continue;

            byte[]? fileData = ReadForkData(dataFork, logicalSize);
            if (fileData == null || fileData.Length < (int)logicalSize)
                continue;

            // Trim to logical size
            if (fileData.Length > (int)logicalSize)
            {
                var trimmed = new byte[(int)logicalSize];
                Array.Copy(fileData, trimmed, (int)logicalSize);
                fileData = trimmed;
            }

            results.Add((fileName, fileData));
        }
    }

    private byte[]? ReadForkData(ReadOnlySpan<byte> forkData, long logicalSize)
    {
        // ForkData: logicalSize(8), clumpSize(4), totalBlocks(4), extents(8 × 8 bytes)
        // Extents start at offset 16
        var parts = new List<byte[]>();
        long totalLen = 0;

        for (int e = 0; e < 8; e++)
        {
            int extOffset = 16 + e * 8;
            uint startBlock = BinaryPrimitives.ReadUInt32BigEndian(forkData.Slice(extOffset, 4));
            uint blockCount = BinaryPrimitives.ReadUInt32BigEndian(forkData.Slice(extOffset + 4, 4));

            if (blockCount == 0)
                break;

            long byteOffset = _volumeOffset + (long)startBlock * _blockSize;
            long byteLen = (long)blockCount * _blockSize;

            if (byteOffset < 0 || byteOffset + byteLen > _disk.Length)
                break;

            var chunk = new byte[byteLen];
            Array.Copy(_disk, byteOffset, chunk, 0, (int)byteLen);
            parts.Add(chunk);
            totalLen += byteLen;
        }

        if (totalLen == 0)
            return null;

        // Concatenate all extents
        var result = new byte[totalLen];
        long pos = 0;
        foreach (var part in parts)
        {
            Array.Copy(part, 0, result, pos, part.Length);
            pos += part.Length;
        }
        return result;
    }
}
