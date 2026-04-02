using System.Buffers.Binary;
using System.Text;

namespace HyperCardSharp.Core.Containers;

/// <summary>
/// Extracts files from StuffIt Classic (.sit) archives.
/// Supports compression methods: 0 (no compression) and 2 (LZW-12).
/// </summary>
public class StuffItExtractor : IContainerExtractor
{
    // Archive header: 22 bytes
    // 0x00: "SIT!" (4)
    // 0x04: numFiles (2, BE)
    // 0x06: archiveSize (4, BE)
    // 0x0A: "rLau" (4)
    // 0x0E: version (1)
    // 0x0F: reserved (7)
    private const int ArchiveHeaderSize = 22;

    // File entry header: 112 bytes
    private const int EntryHeaderSize = 112;

    // Compression methods
    private const byte MethodNone = 0;
    private const byte MethodRle = 1;
    private const byte MethodLzw = 2;
    private const byte MethodHuffman = 3;
    private const byte MethodLzss = 5;
    private const byte MethodLzssHuffman = 13;

    /// <summary>
    /// Decode a Mac OS Pascal string (length-prefixed, bytes) to a .NET string.
    /// </summary>
    private static string DecodeMacName(ReadOnlySpan<byte> nameBytes)
        => HyperCardSharp.Core.Text.MacRomanEncoding.GetString(nameBytes);

    public bool CanHandle(ReadOnlySpan<byte> data)
    {
        if (data.Length < ArchiveHeaderSize)
            return false;

        // "SIT!" at bytes 0-3 = StuffIt Classic
        if (data[0] == 'S' && data[1] == 'I' && data[2] == 'T' && data[3] == '!')
            return true;

        // "StuffIt " (7 bytes) = StuffIt 5.x / Aladdin format — detected but not extracted
        if (data.Length >= 7 &&
            data[0] == 'S' && data[1] == 't' && data[2] == 'u' && data[3] == 'f' &&
            data[4] == 'f' && data[5] == 'I' && data[6] == 't')
            return true;

        return false;
    }

    /// <summary>Returns true when the data is a StuffIt 5.x archive (not StuffIt Classic).</summary>
    private static bool IsStuffIt5(ReadOnlySpan<byte> data)
        => data.Length >= 7 &&
           data[0] == 'S' && data[1] == 't' && data[2] == 'u' && data[3] == 'f' &&
           data[4] == 'f' && data[5] == 'I' && data[6] == 't';

    public byte[]? Extract(byte[] data)
    {
        if (IsStuffIt5(data))
            return null;  // StuffIt 5.x format: recognised but not yet supported
        var all = ExtractAll(data);
        return all.Count > 0 ? all[0].Data : null;
    }

    /// <summary>
    /// Extracts resource forks for all entries in the archive.
    /// Returns a dictionary mapping file name → decompressed resource fork bytes.
    /// Entries with no resource fork (rsrcLen == 0) are omitted.
    /// </summary>
    public Dictionary<string, byte[]> ExtractAllResourceForks(byte[] data)
    {
        var results = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        if (!CanHandle(data) || IsStuffIt5(data))
            return results;

        var span = data.AsSpan();
        ushort numFiles = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(4, 2));
        if (numFiles == 0)
            return results;

        int pos = ArchiveHeaderSize;

        for (int fileIdx = 0; fileIdx < numFiles; fileIdx++)
        {
            if (pos + EntryHeaderSize > span.Length)
                break;

            var entry = span.Slice(pos, EntryHeaderSize);

            byte rsrcMethod = entry[0];

            int nameLen = entry[2] > 63 ? 63 : entry[2];
            string fileName = nameLen > 0
                ? DecodeMacName(entry.Slice(3, nameLen))
                : $"file_{fileIdx}";

            int rsrcLen     = (int)BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(0x54, 4));
            int rsrcCompLen = (int)BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(0x5C, 4));
            int dataCompLen = (int)BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(0x60, 4));

            pos += EntryHeaderSize;

            if (pos + rsrcCompLen + dataCompLen > span.Length)
                break;

            var rsrcCompData = span.Slice(pos, rsrcCompLen).ToArray();
            pos += rsrcCompLen;
            pos += dataCompLen;  // skip data fork

            if (rsrcLen <= 0)
                continue;

            var rsrc = Decompress(rsrcMethod, rsrcCompData, rsrcLen);
            if (rsrc != null)
                results[fileName] = rsrc;
        }

        return results;
    }

    /// <summary>
    /// Extracts all STAK files found in the archive.
    /// Returns a list of (name, decompressed data) pairs.
    /// </summary>
    public List<(string Name, byte[] Data)> ExtractAll(byte[] data)
    {
        var results = new List<(string Name, byte[] Data)>();

        if (!CanHandle(data) || IsStuffIt5(data))
            return results;

        try
        {
            var span = data.AsSpan();

            ushort numFiles = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(4, 2));
            if (numFiles == 0)
                return results;

            int pos = ArchiveHeaderSize;

            for (int fileIdx = 0; fileIdx < numFiles; fileIdx++)
            {
                if (pos + EntryHeaderSize > span.Length)
                    break;

                var entry = span.Slice(pos, EntryHeaderSize);

                byte rsrcMethod = entry[0];
                byte dataMethod = entry[1];

                int nameLen = entry[2];
                if (nameLen > 63) nameLen = 63;
                string fileName = nameLen > 0
                    ? DecodeMacName(entry.Slice(3, nameLen))
                    : $"stack_{fileIdx}";

                string fileType = Encoding.ASCII.GetString(entry.Slice(0x42, 4));

                int rsrcLen     = (int)BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(0x54, 4));
                int dataLen     = (int)BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(0x58, 4));
                int rsrcCompLen = (int)BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(0x5C, 4));
                int dataCompLen = (int)BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(0x60, 4));

                pos += EntryHeaderSize;

                if (pos + rsrcCompLen + dataCompLen > span.Length)
                    break;

                var rsrcCompData = span.Slice(pos, rsrcCompLen).ToArray();
                pos += rsrcCompLen;

                var dataCompData = span.Slice(pos, dataCompLen).ToArray();
                pos += dataCompLen;

                if (fileType != "STAK")
                    continue;

                // Try data fork first, then resource fork.
                byte[]? extracted = null;
                if (dataLen > 0)
                {
                    var candidate = Decompress(dataMethod, dataCompData, dataLen);
                    if (candidate != null && IsStack(candidate))
                        extracted = candidate;
                }
                if (extracted == null && rsrcLen > 0)
                {
                    var candidate = Decompress(rsrcMethod, rsrcCompData, rsrcLen);
                    if (candidate != null && IsStack(candidate))
                        extracted = candidate;
                }
                // Accept data fork even without confirmed magic
                if (extracted == null && dataLen > 0)
                    extracted = Decompress(dataMethod, dataCompData, dataLen);

                if (extracted != null)
                    results.Add((fileName, extracted));
            }
        }
        catch
        {
            // Partial results are still valid
        }

        return results;
    }

    private static bool IsStack(byte[] data)
    {
        return data.Length >= 8 &&
               data[4] == 'S' && data[5] == 'T' && data[6] == 'A' && data[7] == 'K';
    }

    private static byte[]? Decompress(byte method, byte[] compData, int uncompLen)
    {
        try
        {
            return method switch
            {
                MethodNone => compData,
                MethodRle => DecompressRle(compData, uncompLen),
                MethodLzw => DecompressLzw(compData, uncompLen),
                MethodLzss => DecompressLzss(compData, uncompLen),
                MethodLzssHuffman => StuffIt13Decompressor.Decompress(compData, uncompLen),
                _ => null  // Unsupported method
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// StuffIt Classic LZSS (method 5): Okumura LZSS, 4 KB sliding window.
    /// Parameters: N=4096 (12-bit offset), F=17 (max match), THRESHOLD=2 (min match).
    /// Ring buffer initialised to ASCII spaces (0x20); initial write pointer r = N-F = 4079.
    /// Flag byte: bit 7 processed first; 1 = literal byte, 0 = back-reference.
    /// Back-reference (2 bytes): byte0 = position[7:0], byte1 = position[11:8] | (len-2)<<4.
    /// TODO: verify against real stack extraction if decompressed output shows garbage.
    /// </summary>
    private static byte[]? DecompressLzss(byte[] input, int uncompLen)
    {
        const int N = 4096;         // ring-buffer / window size
        const int F = 17;           // max match length  (1<<EJ) + THRESHOLD - 1  where EJ=4, T=2
        const int Threshold = 2;    // minimum encoded match length

        var ringBuf = new byte[N];
        Array.Fill(ringBuf, (byte)0x20);   // initialise to ASCII spaces
        int r = N - F;                     // initial write position = 4079

        var output = new byte[uncompLen];
        int outPos = 0;
        int inPos = 0;

        while (outPos < uncompLen && inPos < input.Length)
        {
            byte flags = input[inPos++];

            // Process 8 flag bits, MSB (bit 7) first.
            for (int bit = 7; bit >= 0 && outPos < uncompLen; bit--)
            {
                if (inPos >= input.Length) break;

                if ((flags & (1 << bit)) != 0)
                {
                    // Literal byte
                    byte b = input[inPos++];
                    output[outPos++] = b;
                    ringBuf[r] = b;
                    r = (r + 1) & (N - 1);
                }
                else
                {
                    // Back-reference: 2 bytes
                    if (inPos + 1 >= input.Length) break;
                    byte b0 = input[inPos++];
                    byte b1 = input[inPos++];

                    int pos = b0 | ((b1 & 0x0F) << 8);          // 12-bit position
                    int matchLen = (b1 >> 4) + Threshold;        // 4-bit length + threshold

                    for (int k = 0; k < matchLen && outPos < uncompLen; k++)
                    {
                        byte c = ringBuf[(pos + k) & (N - 1)];
                        output[outPos++] = c;
                        ringBuf[r] = c;
                        r = (r + 1) & (N - 1);
                    }
                }
            }
        }

        return output;
    }

    /// <summary>
    /// StuffIt RLE (method 1): 0x90 is the escape byte.
    /// If byte != 0x90 → emit it.
    /// If byte == 0x90, read next: if 0x00 → emit 0x90; else count = next, fill byte = next-next, emit count copies.
    /// </summary>
    private static byte[] DecompressRle(byte[] input, int uncompLen)
    {
        var output = new List<byte>(uncompLen);
        int i = 0;
        while (i < input.Length)
        {
            byte b = input[i++];
            if (b != 0x90)
            {
                output.Add(b);
            }
            else
            {
                if (i >= input.Length) break;
                byte count = input[i++];
                if (count == 0x00)
                {
                    output.Add(0x90);
                }
                else
                {
                    if (i >= input.Length) break;
                    byte fill = input[i++];
                    for (int k = 0; k < count; k++)
                        output.Add(fill);
                }
            }
        }
        return output.ToArray();
    }

    /// <summary>
    /// StuffIt LZW-12 (method 2): variable-width codes, LSB-first bit packing.
    /// Initial code width = 9 bits. Clear code = 256, EOF = 257.
    /// Table grows until 12-bit codes (4096 entries); clear code resets to 9-bit.
    /// </summary>
    private static byte[] DecompressLzw(byte[] input, int uncompLen)
    {
        const int ClearCode = 256;
        const int EofCode = 257;
        const int MinWidth = 9;
        const int MaxWidth = 12;
        const int MaxTable = 1 << MaxWidth; // 4096

        var output = new List<byte>(uncompLen);

        // LZW string table: each entry = prefix + suffix
        // Use arrays for performance
        var prefix = new int[MaxTable];
        var suffix = new byte[MaxTable];

        // Bit reader state (LSB-first)
        int bitBuffer = 0;
        int bitsInBuffer = 0;
        int bytePos = 0;

        int codeWidth = MinWidth;
        int nextCode = EofCode + 1;
        int codeMask = (1 << codeWidth) - 1;

        // Initialize table entries 0-255
        for (int i = 0; i < 256; i++)
        {
            prefix[i] = -1;
            suffix[i] = (byte)i;
        }

        // Helper: read next code (LSB-first bit packing)
        int ReadCode()
        {
            while (bitsInBuffer < codeWidth && bytePos < input.Length)
            {
                bitBuffer |= input[bytePos++] << bitsInBuffer;
                bitsInBuffer += 8;
            }
            if (bitsInBuffer < codeWidth)
                return EofCode;
            int code = bitBuffer & codeMask;
            bitBuffer >>= codeWidth;
            bitsInBuffer -= codeWidth;
            return code;
        }

        // Helper: expand a code to bytes (decode string)
        byte[] Expand(int code)
        {
            var stack = new List<byte>(64);
            int cur = code;
            while (cur >= 0 && cur < nextCode)
            {
                if (cur < 256)
                {
                    stack.Add(suffix[cur]);
                    break;
                }
                stack.Add(suffix[cur]);
                cur = prefix[cur];
                if (stack.Count > uncompLen + 1024) break; // safety
            }
            stack.Reverse();
            return stack.ToArray();
        }

        void ResetTable()
        {
            codeWidth = MinWidth;
            codeMask = (1 << codeWidth) - 1;
            nextCode = EofCode + 1;
        }

        int prevCode = -1;
        byte firstByte = 0;

        while (true)
        {
            int code = ReadCode();

            if (code == EofCode)
                break;

            if (code == ClearCode)
            {
                ResetTable();
                prevCode = -1;
                continue;
            }

            byte[] str;
            if (code < nextCode)
            {
                str = Expand(code);
            }
            else if (code == nextCode && prevCode >= 0)
            {
                // Special case: code == nextCode (KwKwK sequence)
                var prev = Expand(prevCode);
                str = new byte[prev.Length + 1];
                prev.CopyTo(str, 0);
                str[prev.Length] = firstByte;
            }
            else
            {
                break; // invalid code
            }

            output.AddRange(str);
            firstByte = str[0];

            if (prevCode >= 0 && nextCode < MaxTable)
            {
                prefix[nextCode] = prevCode;
                suffix[nextCode] = firstByte;
                nextCode++;

                // Grow code width when table fills current width
                if (nextCode > codeMask && codeWidth < MaxWidth)
                {
                    codeWidth++;
                    codeMask = (1 << codeWidth) - 1;
                }
            }

            prevCode = code;

            if (output.Count >= uncompLen)
                break;
        }

        return output.ToArray();
    }
}
