using System.Buffers.Binary;
using System.Text;

namespace HyperCardSharp.Core.Containers;

/// <summary>
/// Extracts data from BinHex 4.0 (.hqx) encoded files.
/// BinHex encodes Mac files (data fork + resource fork) as 7-bit ASCII text
/// using a 6-to-8-bit encoding with run-length compression.
/// </summary>
internal class BinHexExtractor : IContainerExtractor
{
    private const byte RleEscape = 0x90;

    private static readonly string BinHexHeader = "(This file must be converted with BinHex 4.0)";

    // 64-character alphabet: each char maps to its index (0-63)
    private static readonly string BinHexAlphabet =
        "!\"#$%&'()*+,-012345689@ABCDEFGHIJKLMNPQRSTUVXYZ[`abcdefhijklmpqr";

    // Reverse lookup: ASCII byte value -> 6-bit value (0-63), or -1 if invalid
    private static readonly int[] CharToValue = BuildCharToValue();

    private static int[] BuildCharToValue()
    {
        var table = new int[128];
        Array.Fill(table, -1);
        for (int i = 0; i < BinHexAlphabet.Length; i++)
            table[BinHexAlphabet[i]] = i;
        return table;
    }

    public bool CanHandle(ReadOnlySpan<byte> data)
    {
        if (data.Length < BinHexHeader.Length + 4) // header + at least ":"...":"
            return false;

        // Search for the BinHex header line in the ASCII text.
        // Limit search to first 4KB to avoid scanning huge files.
        int searchLen = Math.Min(data.Length, 4096);
        var text = Encoding.ASCII.GetString(data.Slice(0, searchLen));
        return text.Contains(BinHexHeader, StringComparison.Ordinal);
    }

    public byte[]? Extract(byte[] data)
    {
        if (!CanHandle(data))
            return null;

        try
        {
            var text = Encoding.ASCII.GetString(data);

            // Find the header line
            int headerIdx = text.IndexOf(BinHexHeader, StringComparison.Ordinal);
            if (headerIdx < 0)
                return null;

            // Find the first ':' after the header (start delimiter)
            int startColon = text.IndexOf(':', headerIdx + BinHexHeader.Length);
            if (startColon < 0)
                return null;

            // Find the closing ':' (end delimiter)
            int endColon = text.IndexOf(':', startColon + 1);
            if (endColon < 0)
                return null;

            // Extract the encoded data between colons, stripping whitespace
            var encoded = new StringBuilder(endColon - startColon);
            for (int i = startColon + 1; i < endColon; i++)
            {
                char c = text[i];
                if (c > ' ') // skip whitespace and control chars
                    encoded.Append(c);
            }

            // Decode 6-to-8-bit
            byte[] decoded = Decode6to8(encoded.ToString());

            // Reverse RLE
            byte[] expanded = DecodeRle(decoded);

            // Parse the BinHex binary header
            return ParseBinHexData(expanded);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Decodes BinHex 6-to-8-bit encoding. Every 4 encoded characters produce 3 bytes.
    /// </summary>
    private static byte[] Decode6to8(string encoded)
    {
        // Each char is 6 bits. 4 chars = 24 bits = 3 bytes.
        int fullGroups = encoded.Length / 4;
        int remaining = encoded.Length % 4;

        // Output size: full groups * 3 + partial
        int partialBytes = remaining switch
        {
            0 => 0,
            2 => 1, // 12 bits -> 1 byte
            3 => 2, // 18 bits -> 2 bytes
            _ => 0  // 1 leftover char is invalid, but handle gracefully
        };

        var output = new byte[fullGroups * 3 + partialBytes];
        int outIdx = 0;

        for (int i = 0; i < encoded.Length; i += 4)
        {
            int charsLeft = Math.Min(4, encoded.Length - i);

            // Accumulate 6-bit values into a 24-bit buffer
            int accum = 0;
            for (int j = 0; j < charsLeft; j++)
            {
                char c = encoded[i + j];
                int val = (c < 128) ? CharToValue[c] : -1;
                if (val < 0) val = 0; // treat invalid as 0
                accum = (accum << 6) | val;
            }

            // Shift left to fill 24 bits if we had fewer than 4 chars
            accum <<= (4 - charsLeft) * 6;

            // Extract bytes from the 24-bit accumulator
            int bytesToWrite = charsLeft switch
            {
                4 => 3,
                3 => 2,
                2 => 1,
                _ => 0
            };

            if (bytesToWrite >= 1) output[outIdx++] = (byte)((accum >> 16) & 0xFF);
            if (bytesToWrite >= 2) output[outIdx++] = (byte)((accum >> 8) & 0xFF);
            if (bytesToWrite >= 3) output[outIdx++] = (byte)(accum & 0xFF);
        }

        return output;
    }

    /// <summary>
    /// Reverses BinHex run-length encoding.
    /// Escape byte 0x90 followed by 0x00 = literal 0x90.
    /// Escape byte 0x90 followed by N (1-255) = repeat previous byte N-1 more times.
    /// </summary>
    private static byte[] DecodeRle(byte[] data)
    {
        var output = new List<byte>(data.Length);

        for (int i = 0; i < data.Length; i++)
        {
            byte b = data[i];

            if (b == RleEscape)
            {
                if (i + 1 >= data.Length)
                    break; // truncated

                byte count = data[++i];
                if (count == 0x00)
                {
                    // Literal 0x90
                    output.Add(RleEscape);
                }
                else
                {
                    // Repeat the previous byte count-1 more times
                    if (output.Count == 0)
                        break; // no previous byte, malformed

                    byte prev = output[output.Count - 1];
                    for (int r = 0; r < count - 1; r++)
                        output.Add(prev);
                }
            }
            else
            {
                output.Add(b);
            }
        }

        return output.ToArray();
    }

    /// <summary>
    /// Parses the BinHex binary structure and returns the data fork (or resource fork for stacks).
    /// </summary>
    private static byte[]? ParseBinHexData(byte[] data)
    {
        if (data.Length < 1)
            return null;

        int nameLen = data[0];
        // Minimum header size: 1 (nameLen) + nameLen + 1 (version) + 4 (type) + 4 (creator)
        //                      + 2 (flags) + 4 (dataLen) + 4 (rsrcLen) + 2 (headerCRC) = nameLen + 22
        int headerSize = nameLen + 22;
        if (data.Length < headerSize)
            return null;

        int offset = 1 + nameLen; // skip name
        offset++; // skip version byte

        // File type (4 bytes)
        string fileType = Encoding.ASCII.GetString(data, offset, 4);
        offset += 4;

        // Creator (4 bytes) - skip
        offset += 4;

        // Flags (2 bytes) - skip
        offset += 2;

        // Data fork length (4 bytes, big-endian)
        int dataForkLen = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset, 4));
        offset += 4;

        // Resource fork length (4 bytes, big-endian)
        int rsrcForkLen = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset, 4));
        offset += 4;

        // Header CRC (2 bytes) - skip
        offset += 2;

        // Data fork
        if (dataForkLen < 0 || offset + dataForkLen > data.Length)
            return null;

        byte[] dataFork = data.AsSpan(offset, dataForkLen).ToArray();
        offset += dataForkLen;

        // Data CRC (2 bytes) - skip
        offset += 2;

        // Resource fork
        if (fileType == "STAK" && dataForkLen > 0)
            return dataFork;

        // For non-STAK types, still return the data fork — the pipeline will
        // recursively detect the inner format.
        if (dataForkLen > 0)
            return dataFork;

        // If data fork is empty, try the resource fork
        if (rsrcForkLen > 0 && offset + rsrcForkLen <= data.Length)
            return data.AsSpan(offset, rsrcForkLen).ToArray();

        return null;
    }
}
