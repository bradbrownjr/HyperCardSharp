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
    private const byte MethodLzssHuffman = 13;

    public bool CanHandle(ReadOnlySpan<byte> data)
    {
        if (data.Length < ArchiveHeaderSize)
            return false;

        // "SIT!" at bytes 0-3
        return data[0] == 'S' && data[1] == 'I' && data[2] == 'T' && data[3] == '!';
    }

    public byte[]? Extract(byte[] data)
    {
        if (!CanHandle(data))
            return null;

        try
        {
            var span = data.AsSpan();

            ushort numFiles = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(4, 2));
            if (numFiles == 0)
                return null;

            // Validate "rLau" secondary magic
            // (offset 0x0A = 10)
            // Not strictly required — some archivers may vary. We proceed regardless.

            int pos = ArchiveHeaderSize;

            for (int fileIdx = 0; fileIdx < numFiles; fileIdx++)
            {
                if (pos + EntryHeaderSize > span.Length)
                    break;

                var entry = span.Slice(pos, EntryHeaderSize);

                byte rsrcMethod = entry[0];
                byte dataMethod = entry[1];

                // Pascal string at offset 2 (64 bytes total)
                int nameLen = entry[2];
                if (nameLen > 63) nameLen = 63;

                // File type at offset 0x42
                string fileType = Encoding.ASCII.GetString(entry.Slice(0x42, 4));

                // Sizes
                int rsrcLen = (int)BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(0x54, 4));
                int dataLen = (int)BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(0x58, 4));
                int rsrcCompLen = (int)BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(0x5C, 4));
                int dataCompLen = (int)BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(0x60, 4));

                pos += EntryHeaderSize;

                // Bounds check compressed data
                if (pos + rsrcCompLen + dataCompLen > span.Length)
                    break;

                // Resource fork data
                var rsrcCompData = span.Slice(pos, rsrcCompLen).ToArray();
                pos += rsrcCompLen;

                // Data fork data
                var dataCompData = span.Slice(pos, dataCompLen).ToArray();
                pos += dataCompLen;

                // We want STAK files
                if (fileType == "STAK")
                {
                    // Try data fork first
                    if (dataLen > 0)
                    {
                        var extracted = Decompress(dataMethod, dataCompData, dataLen);
                        if (extracted != null && IsStack(extracted))
                            return extracted;
                    }

                    // Try resource fork
                    if (rsrcLen > 0)
                    {
                        var extracted = Decompress(rsrcMethod, rsrcCompData, rsrcLen);
                        if (extracted != null && IsStack(extracted))
                            return extracted;
                    }

                    // Return data fork even if not confirmed STAK magic (maybe it works)
                    if (dataLen > 0)
                    {
                        return Decompress(dataMethod, dataCompData, dataLen);
                    }
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
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
