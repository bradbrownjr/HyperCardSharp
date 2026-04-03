using System.Buffers.Binary;
using System.Text;

namespace HyperCardSharp.Core.Containers;

/// <summary>Describes a file found on an HFS volume.</summary>
public record HfsFileEntry(string Name, string Type, string Creator, int ParentId, int DataForkSize, int ResourceForkSize);

/// <summary>
/// Parses an HFS filesystem image and extracts files with type "STAK".
/// Supports reading the Master Directory Block, catalog B-tree leaf nodes,
/// and file data via extent descriptors.
/// </summary>
public class HfsReader
{
    // HFS volume constants
    private const int SectorSize = 512;
    private const ushort HfsMdbSignature = 0xD2D7;
    private const int MdbOffset = SectorSize * 2; // block 2

    // Some disk-image tools produce an MDB with a non-standard signature word while
    // keeping all other MDB fields intact.  Rather than hard-coding every variant we
    // fall back to a heuristic check: the creation-date field (at MDB+2) must look
    // like a plausible Mac timestamp (seconds since 1904-01-01; valid range covers
    // roughly 1980–2040 which maps to ≈0xAB8F7380–0x1040B3800 in 32-bit unsigned).
    private const uint HfsMdbTimestampMin = 0xA8000000u;  // ≈ 1981
    private const uint HfsMdbTimestampMax = 0xF8000000u;  // ≈ 2049

    // MDB field offsets (from start of MDB)
    private const int MdbSigWord = 0x00;
    private const int MdbNmAlBlks = 0x12;
    private const int MdbAlBlkSiz = 0x14;
    private const int MdbAlBlSt = 0x1C;
    private const int MdbCtFlSize = 0x92;
    private const int MdbCtExtRec = 0x96;

    // B-tree node descriptor offsets
    private const int BtNodeKindOffset = 8;
    private const int BtNumRecordsOffset = 10;
    private const int BtNodeSize = 512;

    // B-tree header record offsets (within header node, starting at offset 14)
    private const int BtHdrFirstLeaf = 0x18 - 0x0E; // relative to header record start (= 10 from offset 14 = absolute 24)

    // Catalog record types
    private const short CatalogFileRecord = 2;

    // CdrFilRec offsets from start of data record.
    // Layout (Inside Macintosh: Files, §2.76): cdrType(1)+cdrResrv2(1)+filFlags(2)+filUsrWds(16)
    //   +filFlNum(4)+filStBlk(2)+filLgLen(4)+filPyLen(4)+filRStBlk(2)+filRLgLen(4)+filRPyLen(4)
    //   +filCrDat(4)+filMdDat(4)+filBkDat(4)+filFndrInfo(16)+filClpSize(2)+filExtRec(12)+filRExtRec(12)
    private const int FilDataLogEofOffset = 0x1A;   // data fork logical EOF (offset 26)
    private const int FilRsrcLogEofOffset = 0x24;   // rsrc fork logical EOF (offset 36)
    private const int FilDataExtOffset    = 0x4A;   // data fork extent record (offset 74, 3 × 4 bytes)
    private const int FilRsrcExtOffset    = 0x56;   // rsrc fork extent record (offset 86, 3 × 4 bytes)

    private readonly byte[] _disk;

    public HfsReader(byte[] diskImage)
    {
        _disk = diskImage;
    }

    /// <summary>
    /// Returns true if this looks like an HFS volume.
    /// Accepts the canonical D2D7 signature and also tries a heuristic check
    /// (plausible creation-date timestamp) for disk images produced by tools
    /// that write non-standard signature words.
    /// </summary>
    public bool IsHfs()
    {
        if (_disk.Length < MdbOffset + 10)
            return false;
        ushort sig = BinaryPrimitives.ReadUInt16BigEndian(_disk.AsSpan(MdbOffset, 2));
        if (sig == HfsMdbSignature)
            return true;
        // Heuristic: the standard D2D7 sigword is sometimes replaced by imaging
        // tools.  If the creation-date field at MDB+2 contains a plausible Mac
        // timestamp, treat the volume as HFS anyway.
        uint crDate = BinaryPrimitives.ReadUInt32BigEndian(_disk.AsSpan(MdbOffset + 2, 4));
        return crDate >= HfsMdbTimestampMin && crDate <= HfsMdbTimestampMax;
    }

    /// <summary>
    /// Enumerate all STAK files on the volume, returning their names and data forks.
    /// </summary>
    public List<(string Name, byte[] Data)> EnumerateStacks()
    {
        var results = new List<(string Name, byte[] Data)>();
        if (!IsHfs())
            return results;

        try
        {
            var mdb = _disk.AsSpan(MdbOffset);

            int nmAlBlks = BinaryPrimitives.ReadUInt16BigEndian(mdb.Slice(MdbNmAlBlks, 2));
            int alBlkSiz = (int)BinaryPrimitives.ReadUInt32BigEndian(mdb.Slice(MdbAlBlkSiz, 4));
            int alBlSt = BinaryPrimitives.ReadUInt16BigEndian(mdb.Slice(MdbAlBlSt, 2));

            var ctExtRec = mdb.Slice(MdbCtExtRec, 12);
            var catalogData = ReadExtents(ctExtRec, alBlSt, alBlkSiz);
            if (catalogData == null || catalogData.Length < BtNodeSize)
                return results;

            int firstLeaf = GetFirstLeafNode(catalogData);
            if (firstLeaf < 0)
                return results;

            int nodeNum = firstLeaf;
            while (nodeNum > 0)
            {
                int nodeOffset = nodeNum * BtNodeSize;
                if (nodeOffset + BtNodeSize > catalogData.Length)
                    break;

                var node = catalogData.AsSpan(nodeOffset, BtNodeSize);

                byte kind = node[BtNodeKindOffset];
                if (kind != 0xFF)
                {
                    int fLink = (int)BinaryPrimitives.ReadUInt32BigEndian(node.Slice(0, 4));
                    nodeNum = fLink;
                    continue;
                }

                ushort numRecords = BinaryPrimitives.ReadUInt16BigEndian(node.Slice(BtNumRecordsOffset, 2));
                ScanLeafNodeAll(node, numRecords, alBlSt, alBlkSiz, results);

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

    /// <summary>
    /// Find and return the data fork of the first STAK file on the volume.
    /// </summary>
    public byte[]? ExtractFirstStack()
    {
        if (!IsHfs())
            return null;

        try
        {
            var mdb = _disk.AsSpan(MdbOffset);

            // Read MDB fields
            int nmAlBlks = BinaryPrimitives.ReadUInt16BigEndian(mdb.Slice(MdbNmAlBlks, 2));
            int alBlkSiz = (int)BinaryPrimitives.ReadUInt32BigEndian(mdb.Slice(MdbAlBlkSiz, 4));
            int alBlSt = BinaryPrimitives.ReadUInt16BigEndian(mdb.Slice(MdbAlBlSt, 2));

            // Catalog file extent record at MDB offset 0x96 (3 extents × 4 bytes each)
            // Each extent: startBlock (2, BE), blockCount (2, BE)
            var ctExtRec = mdb.Slice(MdbCtExtRec, 12);

            // Build catalog file byte array from extents
            var catalogData = ReadExtents(ctExtRec, alBlSt, alBlkSiz);
            if (catalogData == null || catalogData.Length < BtNodeSize)
                return null;

            // Parse B-tree header node (node 0) to find first leaf node
            int firstLeaf = GetFirstLeafNode(catalogData);
            if (firstLeaf < 0)
                return null;

            // Iterate all leaf nodes starting from firstLeaf
            int nodeNum = firstLeaf;
            while (nodeNum > 0)
            {
                int nodeOffset = nodeNum * BtNodeSize;
                if (nodeOffset + BtNodeSize > catalogData.Length)
                    break;

                var node = catalogData.AsSpan(nodeOffset, BtNodeSize);

                byte kind = node[BtNodeKindOffset];
                // 0xFF = leaf node
                if (kind != 0xFF)
                {
                    // Not a leaf — follow fLink anyway to be safe
                    int fLink = (int)BinaryPrimitives.ReadUInt32BigEndian(node.Slice(0, 4));
                    nodeNum = fLink;
                    continue;
                }

                ushort numRecords = BinaryPrimitives.ReadUInt16BigEndian(node.Slice(BtNumRecordsOffset, 2));

                // Record offsets are stored at end of node, 2 bytes each, in reverse
                // offset table entry i: node[512 - 2*(i+1)] = offset of record i from node start
                var stackData = ScanLeafNode(node, numRecords, alBlSt, alBlkSiz);
                if (stackData != null)
                    return stackData;

                // Follow forward link (fLink at node offset 0)
                int nextNode = (int)BinaryPrimitives.ReadUInt32BigEndian(node.Slice(0, 4));
                nodeNum = nextNode;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private byte[]? ScanLeafNode(ReadOnlySpan<byte> node, ushort numRecords, int alBlSt, int alBlkSiz)
    {
        for (int r = 0; r < numRecords; r++)
        {
            // Offset table at end of node: entry r is at node[512 - 2*(r+1)]
            int tablePos = BtNodeSize - 2 * (r + 1);
            if (tablePos < 14)
                break;

            ushort recOffset = BinaryPrimitives.ReadUInt16BigEndian(node.Slice(tablePos, 2));
            if (recOffset < 14 || recOffset >= BtNodeSize)
                continue;

            var rec = node.Slice(recOffset);

            // Key: keyLen(1), reserved(1), parentID(4), name(Pascal string)
            int keyLen = rec[0];
            if (keyLen < 6 || recOffset + 1 + keyLen > BtNodeSize)
                continue;

            // After key (aligned to 2 bytes)
            int dataStart = recOffset + 1 + keyLen;
            if ((dataStart & 1) != 0) dataStart++; // word align
            if (dataStart + 2 > BtNodeSize)
                continue;

            // Record type: cdrType is a 1-byte field at dataStart[0], followed by 1-byte cdrResrv2.
            // Reading as int16BE gives 0x0200=512 for a file record, not 2 — compare byte only.
            if (node[dataStart] != CatalogFileRecord)
                continue;

            // File record: finder info at data offset 4 (first 4 bytes = file type)
            int finderInfoOff = dataStart + 4;
            if (finderInfoOff + 4 > BtNodeSize)
                continue;

            string fileType = Encoding.ASCII.GetString(node.Slice(finderInfoOff, 4));
            if (fileType != "STAK")
                continue;

            // Data fork: dataLogEOF at data offset 0x1A (filLgLen)
            int dataLogEofOff = dataStart + FilDataLogEofOffset;
            if (dataLogEofOff + 4 > BtNodeSize)
                continue;

            int dataLogEof = (int)BinaryPrimitives.ReadUInt32BigEndian(node.Slice(dataLogEofOff, 4));
            if (dataLogEof <= 0)
                continue;

            // Data fork extents at data offset 0x4A (3 extents × 4 bytes, filExtRec)
            int dataExtOff = dataStart + FilDataExtOffset;
            if (dataExtOff + 12 > BtNodeSize)
                continue;

            var dataExtRec = node.Slice(dataExtOff, 12);
            var fileData = ReadExtents(dataExtRec, alBlSt, alBlkSiz);
            if (fileData == null || fileData.Length < dataLogEof)
                continue;

            // Trim to logical EOF
            if (fileData.Length > dataLogEof)
            {
                var trimmed = new byte[dataLogEof];
                Array.Copy(fileData, trimmed, dataLogEof);
                return trimmed;
            }
            return fileData;
        }
        return null;
    }

    private void ScanLeafNodeAll(ReadOnlySpan<byte> node, ushort numRecords, int alBlSt, int alBlkSiz, List<(string Name, byte[] Data)> results)
    {
        for (int r = 0; r < numRecords; r++)
        {
            int tablePos = BtNodeSize - 2 * (r + 1);
            if (tablePos < 14)
                break;

            ushort recOffset = BinaryPrimitives.ReadUInt16BigEndian(node.Slice(tablePos, 2));
            if (recOffset < 14 || recOffset >= BtNodeSize)
                continue;

            var rec = node.Slice(recOffset);

            // Key: keyLen(1), reserved(1), parentID(4), nameLen(1), name(nameLen bytes)
            int keyLen = rec[0];
            if (keyLen < 6 || recOffset + 1 + keyLen > BtNodeSize)
                continue;

            // Extract filename: Pascal string at offset 6 in the key (length byte at offset 6, chars starting at 7)
            int nameLen = rec[6];
            string fileName = nameLen > 0 && 7 + nameLen <= 1 + keyLen
                ? Encoding.ASCII.GetString(node.Slice(recOffset + 7, nameLen))
                : "";

            // After key (aligned to 2 bytes)
            int dataStart = recOffset + 1 + keyLen;
            if ((dataStart & 1) != 0) dataStart++;
            if (dataStart + 2 > BtNodeSize)
                continue;

            if (node[dataStart] != CatalogFileRecord)
                continue;

            int finderInfoOff = dataStart + 4;
            if (finderInfoOff + 4 > BtNodeSize)
                continue;

            string fileType = Encoding.ASCII.GetString(node.Slice(finderInfoOff, 4));
            if (fileType != "STAK")
                continue;

            int dataLogEofOff = dataStart + FilDataLogEofOffset;
            if (dataLogEofOff + 4 > BtNodeSize)
                continue;

            int dataLogEof = (int)BinaryPrimitives.ReadUInt32BigEndian(node.Slice(dataLogEofOff, 4));
            if (dataLogEof <= 0)
                continue;

            int dataExtOff = dataStart + FilDataExtOffset;
            if (dataExtOff + 12 > BtNodeSize)
                continue;

            var dataExtRec = node.Slice(dataExtOff, 12);
            var fileData = ReadExtents(dataExtRec, alBlSt, alBlkSiz);
            if (fileData == null || fileData.Length < dataLogEof)
                continue;

            if (fileData.Length > dataLogEof)
            {
                var trimmed = new byte[dataLogEof];
                Array.Copy(fileData, trimmed, dataLogEof);
                fileData = trimmed;
            }

            results.Add((fileName, fileData));
        }
    }

    private int GetFirstLeafNode(byte[] catalogData)
    {
        if (catalogData.Length < BtNodeSize)
            return -1;

        // B-tree header node is node 0
        var headerNode = catalogData.AsSpan(0, BtNodeSize);

        byte kind = headerNode[BtNodeKindOffset];
        // Header node kind = 1 (kBTHeaderNode). Some images may store 0x01 or 0x02; accept both for safety.
        if (kind != 1 && kind != 2)
            return -1;

        // B-tree header record starts at offset 14 (after 14-byte node descriptor)
        // firstLeaf is at header record offset 0x18 - 0x0E = 10 (absolute offset 14 + 10 = 24)
        if (headerNode.Length < 28)
            return -1;

        int firstLeaf = (int)BinaryPrimitives.ReadUInt32BigEndian(headerNode.Slice(14 + 10, 4));
        return firstLeaf;
    }

    private byte[]? ReadExtents(ReadOnlySpan<byte> extRec, int alBlSt, int alBlkSiz)
    {
        // 3 extents, each 4 bytes: startBlock(2), blockCount(2)
        var parts = new List<byte[]>();
        int totalLen = 0;

        for (int e = 0; e < 3; e++)
        {
            int startBlock = BinaryPrimitives.ReadUInt16BigEndian(extRec.Slice(e * 4, 2));
            int blockCount = BinaryPrimitives.ReadUInt16BigEndian(extRec.Slice(e * 4 + 2, 2));

            if (blockCount == 0)
                break;
            if (startBlock == 0)
                continue;

            // Byte offset: drAlBlSt * 512 + startBlock * drAlBlkSiz
            int byteOffset = alBlSt * SectorSize + startBlock * alBlkSiz;
            int byteLen = blockCount * alBlkSiz;

            if (byteOffset < 0 || byteOffset + byteLen > _disk.Length)
                break;

            var chunk = new byte[byteLen];
            Array.Copy(_disk, byteOffset, chunk, 0, byteLen);
            parts.Add(chunk);
            totalLen += byteLen;
        }

        if (totalLen == 0)
            return null;

        // Concatenate all extents
        var result = new byte[totalLen];
        int pos = 0;
        foreach (var part in parts)
        {
            Array.Copy(part, 0, result, pos, part.Length);
            pos += part.Length;
        }
        return result;
    }

    /// <summary>
    /// Scan the catalog for all STAK files and return their resource fork bytes,
    /// keyed by file name.  Files with no resource fork data are omitted.
    /// </summary>
    public Dictionary<string, byte[]> EnumerateResourceForks()
        => EnumerateResourceForksCore("STAK");

    /// <summary>
    /// Scan the catalog for ALL files (any type) and return every non-empty resource fork,
    /// keyed by file name.  Useful for finding ICON resources in companion files when the
    /// STAK file itself carries no resource fork.
    /// </summary>
    public Dictionary<string, byte[]> EnumerateAllResourceForks()
        => EnumerateResourceForksCore(null);

    // Core implementation; fileTypeFilter == null means accept any file type.
    private Dictionary<string, byte[]> EnumerateResourceForksCore(string? fileTypeFilter)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (!IsHfs())
            return result;

        try
        {
            var mdb = _disk.AsSpan(MdbOffset);
            int alBlkSiz = (int)BinaryPrimitives.ReadUInt32BigEndian(mdb.Slice(MdbAlBlkSiz, 4));
            int alBlSt   = BinaryPrimitives.ReadUInt16BigEndian(mdb.Slice(MdbAlBlSt, 2));

            var ctExtRec    = mdb.Slice(MdbCtExtRec, 12);
            var catalogData = ReadExtents(ctExtRec, alBlSt, alBlkSiz);
            if (catalogData == null || catalogData.Length < BtNodeSize)
                return result;

            int firstLeaf = GetFirstLeafNode(catalogData);
            if (firstLeaf < 0)
                return result;

            int nodeNum = firstLeaf;
            while (nodeNum > 0)
            {
                int nodeOffset = nodeNum * BtNodeSize;
                if (nodeOffset + BtNodeSize > catalogData.Length)
                    break;

                var node = catalogData.AsSpan(nodeOffset, BtNodeSize);
                if (node[BtNodeKindOffset] == 0xFF) // leaf node
                    ScanLeafNodeForResourceForks(node, BinaryPrimitives.ReadUInt16BigEndian(node.Slice(BtNumRecordsOffset, 2)), alBlSt, alBlkSiz, result, fileTypeFilter);

                int nextNode = (int)BinaryPrimitives.ReadUInt32BigEndian(node.Slice(0, 4));
                nodeNum = nextNode;
            }
        }
        catch
        {
            // Return whatever was collected before the fault.
        }

        return result;
    }

    /// <summary>
    /// Enumerate ALL files on the volume (any type code), returning name, Finder type,
    /// Finder creator, data fork size, and resource fork size. For diagnostics and
    /// resource extraction.
    /// </summary>
    public List<HfsFileEntry> EnumerateAllFiles()
    {
        var results = new List<HfsFileEntry>();
        if (!IsHfs())
            return results;

        try
        {
            var mdb = _disk.AsSpan(MdbOffset);
            int alBlkSiz = (int)BinaryPrimitives.ReadUInt32BigEndian(mdb.Slice(MdbAlBlkSiz, 4));
            int alBlSt   = BinaryPrimitives.ReadUInt16BigEndian(mdb.Slice(MdbAlBlSt, 2));

            var ctExtRec    = mdb.Slice(MdbCtExtRec, 12);
            var catalogData = ReadExtents(ctExtRec, alBlSt, alBlkSiz);
            if (catalogData == null || catalogData.Length < BtNodeSize)
                return results;

            int firstLeaf = GetFirstLeafNode(catalogData);
            if (firstLeaf < 0)
                return results;

            int nodeNum = firstLeaf;
            while (nodeNum > 0)
            {
                int nodeOffset = nodeNum * BtNodeSize;
                if (nodeOffset + BtNodeSize > catalogData.Length)
                    break;

                var node = catalogData.AsSpan(nodeOffset, BtNodeSize);
                if (node[BtNodeKindOffset] == 0xFF)
                    ScanLeafNodeForAllFiles(node, BinaryPrimitives.ReadUInt16BigEndian(node.Slice(BtNumRecordsOffset, 2)), results);

                nodeNum = (int)BinaryPrimitives.ReadUInt32BigEndian(node.Slice(0, 4));
            }
        }
        catch { }

        return results;
    }

    private void ScanLeafNodeForAllFiles(ReadOnlySpan<byte> node, ushort numRecords, List<HfsFileEntry> results)
    {
        for (int r = 0; r < numRecords; r++)
        {
            int tablePos = BtNodeSize - 2 * (r + 1);
            if (tablePos < 14) break;

            ushort recOffset = BinaryPrimitives.ReadUInt16BigEndian(node.Slice(tablePos, 2));
            if (recOffset < 14 || recOffset >= BtNodeSize) continue;

            var rec = node.Slice(recOffset);
            int keyLen = rec[0];
            if (keyLen < 6 || recOffset + 1 + keyLen > BtNodeSize) continue;

            int parentId = (int)BinaryPrimitives.ReadUInt32BigEndian(node.Slice(recOffset + 2, 4));

            int nameLen = rec[6];
            string fileName = nameLen > 0 && 7 + nameLen <= 1 + keyLen
                ? Encoding.ASCII.GetString(node.Slice(recOffset + 7, nameLen))
                : "";

            int dataStart = recOffset + 1 + keyLen;
            if ((dataStart & 1) != 0) dataStart++;
            if (dataStart + 2 > BtNodeSize) continue;

            if (node[dataStart] != CatalogFileRecord) continue;

            int finderInfoOff = dataStart + 4;
            if (finderInfoOff + 8 > BtNodeSize) continue;

            string fileType    = Encoding.ASCII.GetString(node.Slice(finderInfoOff, 4));
            string fileCreator = Encoding.ASCII.GetString(node.Slice(finderInfoOff + 4, 4));

            int dataLogEof = (dataStart + FilDataLogEofOffset + 4 <= BtNodeSize)
                ? (int)BinaryPrimitives.ReadUInt32BigEndian(node.Slice(dataStart + FilDataLogEofOffset, 4))
                : 0;
            int rsrcLogEof = (dataStart + FilRsrcLogEofOffset + 4 <= BtNodeSize)
                ? (int)BinaryPrimitives.ReadUInt32BigEndian(node.Slice(dataStart + FilRsrcLogEofOffset, 4))
                : 0;

            results.Add(new HfsFileEntry(fileName, fileType, fileCreator, parentId, dataLogEof, rsrcLogEof));
        }
    }

    // fileTypeFilter == null → accept any file type.
    private void ScanLeafNodeForResourceForks(ReadOnlySpan<byte> node, ushort numRecords, int alBlSt, int alBlkSiz, Dictionary<string, byte[]> results, string? fileTypeFilter)
    {
        for (int r = 0; r < numRecords; r++)
        {
            int tablePos = BtNodeSize - 2 * (r + 1);
            if (tablePos < 14) break;

            ushort recOffset = BinaryPrimitives.ReadUInt16BigEndian(node.Slice(tablePos, 2));
            if (recOffset < 14 || recOffset >= BtNodeSize) continue;

            var rec = node.Slice(recOffset);
            int keyLen = rec[0];
            if (keyLen < 6 || recOffset + 1 + keyLen > BtNodeSize) continue;

            int nameLen = rec[6];
            string fileName = nameLen > 0 && 7 + nameLen <= 1 + keyLen
                ? Encoding.ASCII.GetString(node.Slice(recOffset + 7, nameLen))
                : "";

            int dataStart = recOffset + 1 + keyLen;
            if ((dataStart & 1) != 0) dataStart++;
            if (dataStart + 2 > BtNodeSize) continue;

            if (node[dataStart] != CatalogFileRecord) continue;

            int finderInfoOff = dataStart + 4;
            if (finderInfoOff + 4 > BtNodeSize) continue;

            string fileType = Encoding.ASCII.GetString(node.Slice(finderInfoOff, 4));
            if (fileTypeFilter != null && fileType != fileTypeFilter) continue;

            int rsrcLogEofOff = dataStart + FilRsrcLogEofOffset;
            if (rsrcLogEofOff + 4 > BtNodeSize) continue;

            int rsrcLogEof = (int)BinaryPrimitives.ReadUInt32BigEndian(node.Slice(rsrcLogEofOff, 4));
            if (rsrcLogEof <= 0) continue;  // no resource fork

            int rsrcExtOff = dataStart + FilRsrcExtOffset;
            if (rsrcExtOff + 12 > BtNodeSize) continue;

            var rsrcExtRec = node.Slice(rsrcExtOff, 12);
            var rsrcData = ReadExtents(rsrcExtRec, alBlSt, alBlkSiz);
            if (rsrcData == null || rsrcData.Length < rsrcLogEof) continue;

            if (rsrcData.Length > rsrcLogEof)
            {
                var trimmed = new byte[rsrcLogEof];
                Array.Copy(rsrcData, trimmed, rsrcLogEof);
                rsrcData = trimmed;
            }

            if (!string.IsNullOrEmpty(fileName))
                results[fileName] = rsrcData;
        }
    }
}

