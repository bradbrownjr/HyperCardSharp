namespace HyperCardSharp.Core.Containers;

/// <summary>
/// Decompresses StuffIt method 13 (LZSS + Huffman) compressed data.
/// Algorithm: LZSS with canonical Huffman prefix codes, 64KB sliding window.
/// Three Huffman tables: firstcode (321 symbols), secondcode (321 symbols),
/// offsetcode (10-14 symbols). The decoder alternates between firstcode and
/// secondcode after literals vs matches.
/// Reference: XADMaster/XADStuffIt13Handle.m (The Unarchiver)
/// </summary>
internal static class StuffIt13Decompressor
{
    private const int WindowSize = 65536;
    private const int WindowMask = WindowSize - 1;
    private const int NumLiteralLengthSymbols = 321;
    private const int EndOfStream = 0x140; // symbol 320

    public static byte[]? Decompress(byte[] input, int uncompLen)
    {
        if (input.Length == 0)
            return null;

        try
        {
            return DecompressCore(input, uncompLen);
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? DecompressCore(byte[] input, int uncompLen)
    {
        var output = new byte[uncompLen];
        int outPos = 0;

        var window = new byte[WindowSize];
        int windowPos = 0;

        // Read first byte (raw, not from bit stream)
        int bytePos = 1;
        byte firstByte = input[0];
        int modeCode = (firstByte >> 4) & 0x0F;

        // Bit reader state (LSB-first)
        int bitBuffer = 0;
        int bitsInBuffer = 0;

        int ReadBits(int count)
        {
            while (bitsInBuffer < count)
            {
                if (bytePos >= input.Length)
                    return 0;
                bitBuffer |= input[bytePos++] << bitsInBuffer;
                bitsInBuffer += 8;
            }
            int val = bitBuffer & ((1 << count) - 1);
            bitBuffer >>= count;
            bitsInBuffer -= count;
            return val;
        }

        int ReadBit()
        {
            if (bitsInBuffer == 0)
            {
                if (bytePos >= input.Length) return 0;
                bitBuffer = input[bytePos++];
                bitsInBuffer = 8;
            }
            int bit = bitBuffer & 1;
            bitBuffer >>= 1;
            bitsInBuffer--;
            return bit;
        }

        int DecodeSymbol(int[] table, int tableBits)
        {
            while (bitsInBuffer < tableBits)
            {
                if (bytePos >= input.Length)
                    break;
                bitBuffer |= input[bytePos++] << bitsInBuffer;
                bitsInBuffer += 8;
            }

            int index = bitBuffer & ((1 << tableBits) - 1);
            int sym = table[index * 2];
            int len = table[index * 2 + 1];
            bitBuffer >>= len;
            bitsInBuffer -= len;
            return sym;
        }

        // Tree-based decode for non-canonical explicit codes (metacode)
        // Nodes are [child0, child1, symbol]; children = -1 if absent
        int DecodeTree(int[][] tree)
        {
            int node = 0;
            while (tree[node][0] != -1 || tree[node][1] != -1) // has any child
            {
                int bit = ReadBit();
                int child = tree[node][bit];
                if (child == -1) break; // dead end (shadowed prefix)
                node = child;
            }
            return tree[node][2]; // symbol
        }

        // Build Huffman tables
        int[]? firstTable, secondTable, offsetTable;
        int firstBits, secondBits, offsetBits;

        if (modeCode >= 1 && modeCode <= 5)
        {
            var firstLengths = GetFirstCodeLengths(modeCode);
            var secondLengths = GetSecondCodeLengths(modeCode);
            var offsetLengths = GetOffsetCodeLengths(modeCode);

            (firstTable, firstBits) = BuildLookupTable(firstLengths);
            (secondTable, secondBits) = BuildLookupTable(secondLengths);
            (offsetTable, offsetBits) = BuildLookupTable(offsetLengths);
        }
        else if (modeCode == 0)
        {
            // Dynamic tables via metacode — uses tree decoder because MetaCodes
            // are arbitrary (non-canonical) explicit codes
            var metaTree = BuildExplicitTree(MetaCodes, MetaCodeLengths, 37);

            // Matches XADStuffIt13Handle.m allocAndParseCodeOfSize:metaCode: exactly.
            // The post-switch "lengths[i]=length" ALWAYS runs (no continue/early-out).
            int[] ParseDynamicLengths(int numSymbols)
            {
                var lengths = new int[numSymbols];
                int length = 0;
                for (int i = 0; i < numSymbols; i++)
                {
                    int val = DecodeTree(metaTree);
                    switch (val)
                    {
                        case 31:
                            length = -1;
                            break;
                        case 32:
                            length++;
                            break;
                        case 33:
                            length--;
                            break;
                        case 34:
                            if (ReadBits(1) == 1 && i < numSymbols)
                                lengths[i++] = length;
                            break;
                        case 35:
                        {
                            int repeat = ReadBits(3) + 2;
                            while (repeat-- > 0 && i < numSymbols)
                                lengths[i++] = length;
                            break;
                        }
                        case 36:
                        {
                            int repeat = ReadBits(6) + 10;
                            while (repeat-- > 0 && i < numSymbols)
                                lengths[i++] = length;
                            break;
                        }
                        default:
                            length = val + 1;
                            break;
                    }
                    if (i < numSymbols)
                        lengths[i] = length;
                }
                return lengths;
            }

            var dynFirstLengths = ParseDynamicLengths(NumLiteralLengthSymbols);
            int[] dynSecondLengths;
            if ((firstByte & 0x08) != 0)
            {
                dynSecondLengths = dynFirstLengths;
            }
            else
            {
                dynSecondLengths = ParseDynamicLengths(NumLiteralLengthSymbols);
            }

            int offsetSyms = (firstByte & 0x07) + 10;
            var dynOffsetLengths = ParseDynamicLengths(offsetSyms);

            (firstTable, firstBits) = BuildLookupTable(dynFirstLengths);
            (secondTable, secondBits) = BuildLookupTable(dynSecondLengths);
            (offsetTable, offsetBits) = BuildLookupTable(dynOffsetLengths);
        }
        else
        {
            return null;
        }

        // LZSS decompression loop
        bool useFirst = true;
        while (outPos < uncompLen)
        {
            var table = useFirst ? firstTable : secondTable;
            int bits = useFirst ? firstBits : secondBits;
            int sym = DecodeSymbol(table, bits);

            if (sym < 0x100)
            {
                // Literal byte
                byte b = (byte)sym;
                output[outPos++] = b;
                window[windowPos & WindowMask] = b;
                windowPos++;
                useFirst = true;
            }
            else if (sym == EndOfStream)
            {
                break;
            }
            else
            {
                // Match
                int length;
                if (sym < 0x13E)
                {
                    length = sym - 0x100 + 3;
                }
                else if (sym == 0x13E)
                {
                    length = ReadBits(10) + 65;
                }
                else // 0x13F
                {
                    length = ReadBits(15) + 65;
                }

                // Decode offset
                int offSym = DecodeSymbol(offsetTable, offsetBits);
                int offset;
                if (offSym == 0)
                    offset = 1;
                else if (offSym == 1)
                    offset = 2;
                else
                    offset = (1 << (offSym - 1)) + ReadBits(offSym - 1) + 1;

                // Copy from window
                int srcPos = windowPos - offset;
                for (int i = 0; i < length && outPos < uncompLen; i++)
                {
                    byte b = window[(srcPos + i) & WindowMask];
                    output[outPos++] = b;
                    window[windowPos & WindowMask] = b;
                    windowPos++;
                }

                useFirst = false;
            }
        }

        return output;
    }

    // -----------------------------------------------------------------------
    // Huffman table construction
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds a flat lookup table from canonical Huffman code lengths.
    /// Returns (table, maxBits) where table[i*2] = symbol, table[i*2+1] = length.
    /// </summary>
    private static (int[] table, int maxBits) BuildLookupTable(int[] codeLengths)
    {
        int maxLen = 0;
        foreach (var l in codeLengths)
            if (l > maxLen) maxLen = l;

        if (maxLen == 0)
            return (new int[2], 1);

        // Use full code length for table (temporary allocation, freed after decompression)
        int tableBits = maxLen;
        int tableSize = 1 << tableBits;
        var table = new int[tableSize * 2];

        // Initialize as invalid
        for (int i = 0; i < tableSize * 2; i += 2)
        {
            table[i] = -1;
            table[i + 1] = tableBits;
        }

        // Compute canonical codes (MSB-first)
        var blCount = new int[maxLen + 1];
        for (int i = 0; i < codeLengths.Length; i++)
            if (codeLengths[i] > 0)
                blCount[codeLengths[i]]++;

        var nextCode = new int[maxLen + 1];
        int code = 0;
        for (int bits = 1; bits <= maxLen; bits++)
        {
            code = (code + blCount[bits - 1]) << 1;
            nextCode[bits] = code;
        }

        // Assign codes and fill table — iterate by length then symbol index,
        // matching the reference implementation's ordering. This matters when
        // Kraft sum > 1 (overspecified codes) because longer codes must overwrite
        // shorter codes at colliding table entries.
        for (int len = 1; len <= maxLen; len++)
        {
            for (int sym = 0; sym < codeLengths.Length; sym++)
            {
                if (codeLengths[sym] != len) continue;

                int c = nextCode[len]++;

                // Bit-reverse for LSB-first
                int reversed = 0;
                for (int i = 0; i < len; i++)
                    reversed |= ((c >> (len - 1 - i)) & 1) << i;

                // Fill all entries that match this prefix
                int step = 1 << len;
                for (int idx = reversed; idx < tableSize; idx += step)
                {
                    table[idx * 2] = sym;
                    table[idx * 2 + 1] = len;
                }
            }
        }

        return (table, tableBits);
    }

    /// <summary>
    /// Builds a binary tree from explicit LSB-first code values for tree-based decoding.
    /// Each node is int[3]: [child0, child1, symbol]. Children = -1 if absent, symbol = -1 if not a leaf.
    /// Tree is built LSB-first to match the LSB-first ReadBit() decoder.
    /// </summary>
    private static int[][] BuildExplicitTree(int[] codes, int[] lengths, int count)
    {
        var nodes = new List<int[]> { new[] { -1, -1, -1 } }; // node 0 = root

        for (int sym = 0; sym < count; sym++)
        {
            int len = lengths[sym];
            if (len <= 0) continue;

            int code = codes[sym];
            int node = 0;

            // Walk from LSB to MSB (matching ReadBit() order)
            for (int bitpos = 0; bitpos < len; bitpos++)
            {
                int bit = (code >> bitpos) & 1;
                if (nodes[node][bit] == -1)
                {
                    nodes[node][bit] = nodes.Count;
                    nodes.Add(new[] { -1, -1, -1 });
                }
                node = nodes[node][bit];
            }

            nodes[node][2] = sym; // set symbol at leaf
        }

        return nodes.ToArray();
    }

    // -----------------------------------------------------------------------
    // Preset Huffman code length tables
    // -----------------------------------------------------------------------

    private static int[] GetFirstCodeLengths(int preset) => preset switch
    {
        1 => FirstCodeLengths1,
        2 => FirstCodeLengths2,
        3 => FirstCodeLengths3,
        4 => FirstCodeLengths4,
        5 => FirstCodeLengths5,
        _ => throw new ArgumentOutOfRangeException(nameof(preset))
    };

    private static int[] GetSecondCodeLengths(int preset) => preset switch
    {
        1 => SecondCodeLengths1,
        2 => SecondCodeLengths2,
        3 => SecondCodeLengths3,
        4 => SecondCodeLengths4,
        5 => SecondCodeLengths5,
        _ => throw new ArgumentOutOfRangeException(nameof(preset))
    };

    private static int[] GetOffsetCodeLengths(int preset) => preset switch
    {
        1 => OffsetCodeLengths1,
        2 => OffsetCodeLengths2,
        3 => OffsetCodeLengths3,
        4 => OffsetCodeLengths4,
        5 => OffsetCodeLengths5,
        _ => throw new ArgumentOutOfRangeException(nameof(preset))
    };

    // MetaCodes: explicit bit patterns (LSB-first) for the meta Huffman code
    private static readonly int[] MetaCodes =
    {
        0x5d8, 0x058, 0x040, 0x0c0, 0x000, 0x078, 0x02b, 0x014,
        0x00c, 0x01c, 0x01b, 0x00b, 0x010, 0x020, 0x038, 0x018,
        0x0d8, 0xbd8, 0x180, 0x680, 0x380, 0xf80, 0x780, 0x480,
        0x080, 0x280, 0x3d8, 0xfd8, 0x7d8, 0x9d8, 0x1d8, 0x004,
        0x001, 0x002, 0x007, 0x003, 0x008
    };

    private static readonly int[] MetaCodeLengths =
    {
        11, 8, 8, 8, 8, 7, 6, 5, 5, 5, 5, 6, 5, 6, 7, 7,
         9, 12, 10, 11, 11, 12, 12, 11, 11, 11, 12, 12, 12, 12, 12, 5,
         2, 2, 3, 4, 5
    };

    // --- Preset 1 ---

    private static readonly int[] FirstCodeLengths1 =
    {
         4, 5, 7, 8, 8, 9, 9, 9, 9, 7, 9, 9, 9, 8, 9, 9,
         9, 9, 9, 9, 9, 9, 9,10, 9, 9,10,10, 9,10, 9, 9,
         5, 9, 9, 9, 9,10, 9, 9, 9, 9, 9, 9, 9, 9, 7, 9,
         9, 8, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9,
         9, 8, 9, 9, 8, 8, 9, 9, 9, 9, 9, 9, 9, 7, 8, 9,
         7, 9, 9, 7, 7, 9, 9, 9, 9,10, 9,10,10,10, 9, 9,
         9, 5, 9, 8, 7, 5, 9, 8, 8, 7, 9, 9, 8, 8, 5, 5,
         7,10, 5, 8, 5, 8, 9, 9, 9, 9, 9,10, 9, 9,10, 9,
         9,10,10,10,10,10,10,10, 9,10,10,10,10,10,10,10,
         9,10,10,10,10,10,10,10,10,10,10,10,10,10,10,10,
         9,10,10,10,10,10,10,10, 9, 9,10,10,10,10,10,10,
        10,10,10,10,10,10,10,10,10,10, 9,10,10,10,10,10,
         9,10,10,10,10,10,10,10,10,10,10,10,10,10,10,10,
        10,10,10,10,10,10,10,10,10,10,10,10,10,10,10,10,
         9,10,10,10,10,10,10,10,10,10,10,10, 9, 9,10,10,
         9,10,10,10,10,10,10,10, 9,10,10,10, 9,10, 9, 5,
         6, 5, 5, 8, 9, 9, 9, 9, 9, 9,10,10,10, 9,10,10,
        10,10,10,10,10,10,10,10,10,10,10,10,10,10,10,10,
        10,10,10, 9,10, 9, 9, 9,10, 9,10, 9,10, 9,10, 9,
        10,10,10, 9,10, 9,10,10, 9, 9, 9, 6, 9, 9,10, 9,
         5
    };

    private static readonly int[] SecondCodeLengths1 =
    {
         4, 5, 6, 6, 7, 7, 6, 7, 7, 7, 6, 8, 7, 8, 8, 8,
         8, 9, 6, 9, 8, 9, 8, 9, 9, 9, 8,10, 5, 9, 7, 9,
         6, 9, 8,10, 9,10, 8, 8, 9, 9, 7, 9, 8, 9, 8, 9,
         8, 8, 6, 9, 9, 8, 8, 9, 9,10, 8, 9, 9,10, 8,10,
         8, 8, 8, 8, 8, 9, 7,10, 6, 9, 9,11, 7, 8, 8, 9,
         8,10, 7, 8, 6, 9,10, 9, 9,10, 8,11, 9,11, 9,10,
         9, 8, 9, 8, 8, 8, 8,10, 9, 9,10,10, 8, 9, 8, 8,
         8,11, 9, 8, 8, 9, 9,10, 8,11,10,10, 8,10, 9,10,
         8, 9, 9,11, 9,11, 9,10,10,11,10,12, 9,12,10,11,
        10,11, 9,10,10,11,10,11,10,11,10,11,10,10,10, 9,
         9, 9, 8, 7, 6, 8,11,11, 9,12,10,12, 9,11,11,11,
        10,12,11,11,10,12,10,11,10,10,10,11,10,11,11,11,
         9,12,10,12,11,12,10,11,10,12,11,12,11,12,11,12,
        10,12,11,12,11,11,10,12,10,11,10,12,10,12,10,12,
        10,11,11,11,10,11,11,11,10,12,11,12,10,10,11,11,
         9,12,11,12,10,11,10,12,10,11,10,12,10,11,10, 7,
         5, 4, 6, 6, 7, 7, 7, 8, 8, 7, 7, 6, 8, 6, 7, 7,
         9, 8, 9, 9,10,11,11,11,12,11,10,11,12,11,12,11,
        12,12,12,12,11,12,12,11,12,11,12,11,13,11,12,10,
        13,10,14,14,13,14,15,14,16,15,15,18,18,18, 9,18,
         8
    };

    private static readonly int[] OffsetCodeLengths1 = { 5, 6, 3, 3, 3, 3, 3, 3, 3, 4, 6 };

    // --- Preset 2 ---

    private static readonly int[] FirstCodeLengths2 =
    {
         4, 7, 7, 8, 7, 8, 8, 8, 8, 7, 8, 7, 8, 7, 9, 8,
         8, 8, 9, 9, 9, 9,10,10, 9,10,10,10,10,10, 9, 9,
         5, 9, 8, 9, 9,11,10, 9, 8, 9, 9, 9, 8, 9, 7, 8,
         8, 8, 9, 9, 9, 9, 9,10, 9, 9, 9,10, 9, 9,10, 9,
         8, 8, 7, 7, 7, 8, 8, 9, 8, 8, 9, 9, 8, 8, 7, 8,
         7,10, 8, 7, 7, 9, 9, 9, 9,10,10,11,11,11,10, 9,
         8, 6, 8, 7, 7, 5, 7, 7, 7, 6, 9, 8, 6, 7, 6, 6,
         7, 9, 6, 6, 6, 7, 8, 8, 8, 8, 9,10, 9,10, 9, 9,
         8, 9,10,10, 9,10,10, 9, 9,10,10,10,10,10,10,10,
         9,10,10,11,10,10,10,10,10,10,10,11,10,11,10,10,
         9,11,10,10,10,10,10,10, 9, 9,10,11,10,11,10,11,
        10,12,10,11,10,12,11,12,10,12,10,11,10,11,11,11,
         9,10,11,11,11,12,12,10,10,10,11,11,10,11,10,10,
         9,11,10,11,10,11,11,11,10,11,11,12,11,11,10,10,
        10,11,10,10,11,11,12,10,10,11,11,12,11,11,10,11,
         9,12,10,11,11,11,10,11,10,11,10,11, 9,10, 9, 7,
         3, 5, 6, 6, 7, 7, 8, 8, 8, 9, 9, 9,11,10,10,10,
        12,13,11,12,12,11,13,12,12,11,12,12,13,12,14,13,
        14,13,15,13,14,15,15,14,13,15,15,14,15,14,15,15,
        14,15,13,13,14,15,15,14,14,16,16,15,15,15,12,15,
        10
    };

    private static readonly int[] SecondCodeLengths2 =
    {
         5, 6, 6, 6, 6, 7, 7, 7, 7, 7, 7, 8, 7, 8, 7, 7,
         7, 8, 8, 8, 8, 9, 8, 9, 8, 9, 9, 9, 7, 9, 8, 8,
         6, 9, 8, 9, 8, 9, 8, 9, 8, 9, 8, 9, 8, 9, 8, 8,
         8, 8, 8, 9, 8, 9, 8, 9, 9,10, 8,10, 8, 9, 9, 8,
         8, 8, 7, 8, 8, 9, 8, 9, 7, 9, 8,10, 8, 9, 8, 9,
         8, 9, 8, 8, 8, 9, 9, 9, 9,10, 9,11, 9,10, 9,10,
         8, 8, 8, 9, 8, 8, 8, 9, 9, 8, 9,10, 8, 9, 8, 8,
         8,11, 8, 7, 8, 9, 9, 9, 9,10, 9,10, 9,10, 9, 8,
         8, 9, 9,10, 9,10, 9,10, 8,10, 9,10, 9,11,10,11,
         9,11,10,10,10,11, 9,11, 9,10, 9,11, 9,11,10,10,
         9,10, 9, 9, 8,10, 9,11, 9, 9, 9,11,10,11, 9,11,
         9,11, 9,11,10,11,10,11,10,11, 9,10,10,11,10,10,
         8,10, 9,10,10,11, 9,11, 9,10,10,11, 9,10,10, 9,
         9,10, 9,10, 9,10, 9,10, 9,11, 9,11,10,10, 9,10,
         9,11, 9,11, 9,11, 9,10, 9,11, 9,11, 9,11, 9,10,
         8,11, 9,10, 9,10, 9,10, 8,10, 8, 9, 8, 9, 8, 7,
         4, 4, 5, 6, 6, 6, 7, 7, 7, 7, 8, 8, 8, 7, 8, 8,
         9, 9,10,10,10,10,10,10,11,11,10,10,12,11,11,12,
        12,11,12,12,11,12,12,12,12,12,12,11,12,11,13,12,
        13,12,13,14,14,14,15,13,14,13,14,18,18,17, 7,16,
         9
    };

    private static readonly int[] OffsetCodeLengths2 = { 5, 6, 4, 4, 3, 3, 3, 3, 3, 4, 4, 4, 6 };

    // --- Preset 3 ---

    private static readonly int[] FirstCodeLengths3 =
    {
         6, 6, 6, 6, 6, 9, 8, 8, 4, 9, 8, 9, 8, 9, 9, 9,
         8, 9, 9,10, 8,10,10,10, 9,10,10,10, 9,10,10, 9,
         9, 9, 8,10, 9,10, 9,10, 9,10, 9,10, 9, 9, 8, 9,
         8, 9, 9, 9,10,10,10,10, 9, 9, 9,10, 9,10, 9, 9,
         7, 8, 8, 9, 8, 9, 9, 9, 8, 9, 9,10, 9, 9, 8, 9,
         8, 9, 8, 8, 8, 9, 9, 9, 9, 9,10,10,10,10,10, 9,
         8, 8, 9, 8, 9, 7, 8, 8, 9, 8,10,10, 8, 9, 8, 8,
         8,10, 8, 8, 8, 8, 9, 9, 9, 9,10,10,10,10,10, 9,
         7, 9, 9,10,10,10,10,10, 9,10,10,10,10,10,10, 9,
         9,10,10,10,10,10,10,10,10, 9,10,10,10,10,10,10,
         9,10,10,10,10,10,10,10, 9, 9, 9,10,10,10,10,10,
        10,10,10,10,10,10,10,10,10,10, 9,10,10,10,10, 9,
         8, 9,10,10,10,10,10,10,10,10,10,10, 9,10,10,10,
         9,10,10,10,10,10,10,10,10,10,10,10,10,10,10, 9,
         9,10,10,10,10,10,10, 9,10,10,10,10,10,10, 9, 9,
         9,10,10,10,10,10,10, 9, 9,10, 9, 9, 8, 9, 8, 9,
         4, 6, 6, 6, 7, 8, 8, 9, 9,10,10,10, 9,10,10,10,
        10,10,10,10,10,10,10,10,10,10,10,10,10,10, 7,10,
        10,10, 7,10,10, 7, 7, 7, 7, 7, 6, 7,10, 7, 7,10,
         7, 7, 7, 6, 7, 6, 6, 7, 7, 6, 6, 9, 6, 9,10, 6,
        10
    };

    private static readonly int[] SecondCodeLengths3 =
    {
         5, 6, 6, 6, 6, 7, 7, 7, 6, 8, 7, 8, 7, 9, 8, 8,
         7, 7, 8, 9, 9, 9, 9,10, 8, 9, 9,10, 8,10, 9, 8,
         6,10, 8,10, 8,10, 9, 9, 9, 9, 9,10, 9, 9, 8, 9,
         8, 9, 8, 9, 9,10, 9,10, 9, 9, 8,10, 9,11,10, 8,
         8, 8, 8, 9, 7, 9, 9,10, 8, 9, 8,11, 9,10, 9,10,
         8, 9, 9, 9, 9, 8, 9, 9,10,10,10,12,10,11,10,10,
         8, 9, 9, 9, 8, 9, 8, 8,10, 9,10,11, 8,10, 9, 9,
         8,12, 8, 9, 9, 9, 9, 8, 9,10, 9,12,10,10,10, 8,
         7,11,10, 9,10,11, 9,11, 7,11,10,12,10,12,10,11,
         9,11, 9,12,10,12,10,12,10, 9,11,12,10,12,10,11,
         9,10, 9,10, 9,11,11,12, 9,10, 8,12,11,12, 9,12,
        10,12,10,13,10,12,10,12,10,12,10, 9,10,12,10, 9,
         8,11,10,12,10,12,10,12,10,11,10,12, 8,12,10,11,
        10,10,10,12, 9,11,10,12,10,12,11,12,10, 9,10,12,
         9,10,10,12,10,11,10,11,10,12, 8,12, 9,12, 8,12,
         8,11,10,11,10,11, 9,10, 8,10, 9, 9, 8, 9, 8, 7,
         4, 3, 5, 5, 6, 5, 6, 6, 7, 7, 8, 8, 8, 7, 7, 7,
         9, 8, 9, 9,11, 9,11, 9, 8, 9, 9,11,12,11,12,12,
        13,13,12,13,14,13,14,13,14,13,13,13,12,13,13,12,
        13,13,14,14,13,13,14,14,14,14,15,18,17,18, 8,16,
        10
    };

    private static readonly int[] OffsetCodeLengths3 = { 6, 7, 4, 4, 3, 3, 3, 3, 3, 4, 4, 4, 5, 7 };

    // --- Preset 4 ---

    private static readonly int[] FirstCodeLengths4 =
    {
         2, 6, 6, 7, 7, 8, 7, 8, 7, 8, 8, 9, 8, 9, 9, 9,
         8, 8, 9, 9, 9,10,10, 9, 8,10, 9,10, 9,10, 9, 9,
         6, 9, 8, 9, 9,10, 9, 9, 9,10, 9, 9, 9, 9, 8, 8,
         8, 8, 8, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9,10,10, 9,
         7, 7, 8, 8, 8, 8, 9, 9, 7, 8, 9,10, 8, 8, 7, 8,
         8,10, 8, 8, 8, 9, 8, 9, 9,10, 9,11,10,11, 9, 9,
         8, 7, 9, 8, 8, 6, 8, 8, 8, 7,10, 9, 7, 8, 7, 7,
         8,10, 7, 7, 7, 8, 9, 9, 9, 9,10,11, 9,11,10, 9,
         7, 9,10,10,10,11,11,10,10,11,10,10,10,11,11,10,
         9,10,10,11,10,11,10,11,10,10,10,11,10,11,10,10,
         9,10,10,11,10,10,10,10, 9,10,10,10,10,11,10,11,
        10,11,10,11,11,11,10,12,10,11,10,11,10,11,11,10,
         8,10,10,11,10,11,11,11,10,11,10,11,10,11,11,11,
         9,10,11,11,10,11,11,11,10,11,11,11,10,10,10,10,
        10,11,10,10,11,11,10,10, 9,11,10,10,11,11,10,10,
        10,11,10,10,10,10,10,10, 9,11,10,10, 8,10, 8, 6,
         5, 6, 6, 7, 7, 8, 8, 8, 9,10,11,10,10,11,11,12,
        12,10,11,12,12,12,12,13,13,13,13,13,12,13,13,15,
        14,12,14,15,16,12,12,13,15,14,16,15,17,18,15,17,
        16,15,15,15,15,13,13,10,14,12,13,17,17,18,10,17,
         4
    };

    private static readonly int[] SecondCodeLengths4 =
    {
         4, 5, 6, 6, 6, 6, 7, 7, 6, 7, 7, 9, 6, 8, 8, 7,
         7, 8, 8, 8, 6, 9, 8, 8, 7, 9, 8, 9, 8, 9, 8, 9,
         6, 9, 8, 9, 8,10, 9, 9, 8,10, 8,10, 8, 9, 8, 9,
         8, 8, 7, 9, 9, 9, 9, 9, 8,10, 9,10, 9,10, 9, 8,
         7, 8, 9, 9, 8, 9, 9, 9, 7,10, 9,10, 9, 9, 8, 9,
         8, 9, 8, 8, 8, 9, 9,10, 9, 9, 8,11, 9,11,10,10,
         8, 8,10, 8, 8, 9, 9, 9,10, 9,10,11, 9, 9, 9, 9,
         8, 9, 8, 8, 8,10,10, 9, 9, 8,10,11,10,11,11, 9,
         8, 9,10,11, 9,10,11,11, 9,12,10,10,10,12,11,11,
         9,11,11,12, 9,11, 9,10,10,10,10,12, 9,11,10,11,
         9,11,11,11,10,11,11,12, 9,10,10,12,11,11,10,11,
         9,11,10,11,10,11, 9,11,11, 9, 8,11,10,11,11,10,
         7,12,11,11,11,11,11,12,10,12,11,13,11,10,12,11,
        10,11,10,11,10,11,11,11,10,12,11,11,10,11,10,10,
        10,11,10,12,11,12,10,11, 9,11,10,11,10,11,10,12,
         9,11,11,11, 9,11,10,10, 9,11,10,10, 9,10, 9, 7,
         4, 5, 5, 5, 6, 6, 7, 6, 8, 7, 8, 9, 9, 7, 8, 8,
        10, 9,10,10,12,10,11,11,11,11,10,11,12,11,11,11,
        11,11,13,12,11,12,13,12,12,12,13,11, 9,12,13, 7,
        13,11,13,11,10,11,13,15,15,12,14,15,15,15, 6,15,
         5
    };

    private static readonly int[] OffsetCodeLengths4 = { 3, 6, 5, 4, 2, 3, 3, 3, 4, 4, 6 };

    // --- Preset 5 ---

    private static readonly int[] FirstCodeLengths5 =
    {
         7, 9, 9, 9, 9, 9, 9, 9, 9, 8, 9, 9, 9, 7, 9, 9,
         9, 9, 9, 9, 9, 9, 9,10, 9,10, 9,10, 9,10, 9, 9,
         5, 9, 7, 9, 9, 9, 9, 9, 7, 7, 7, 9, 7, 7, 8, 7,
         8, 8, 7, 7, 9, 9, 9, 9, 7, 7, 7, 9, 9, 9, 9, 9,
         9, 7, 9, 7, 7, 7, 7, 9, 9, 7, 9, 9, 7, 7, 7, 7,
         7, 9, 7, 8, 7, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9,
         9, 7, 8, 7, 7, 7, 8, 8, 6, 7, 9, 7, 7, 8, 7, 5,
         6, 9, 5, 7, 5, 6, 7, 7, 9, 8, 9, 9, 9, 9, 9, 9,
         9, 9,10, 9,10,10,10, 9, 9,10,10,10,10,10,10,10,
         9,10,10,10,10,10,10,10,10,10,10,10, 9,10,10,10,
         9,10,10,10, 9, 9,10, 9, 9, 9, 9,10,10,10,10,10,
        10,10,10,10,10,10, 9,10,10,10,10,10,10,10,10,10,
         9,10,10,10, 9,10,10,10, 9, 9, 9,10,10,10,10,10,
         9,10, 9,10,10, 9,10,10, 9,10,10,10,10,10,10,10,
         9,10,10,10,10,10,10,10,10,10,10,10,10,10,10,10,
         9,10,10,10,10,10,10,10, 9,10, 9,10, 9,10,10, 9,
         5, 6, 8, 8, 7, 7, 7, 9, 9, 9, 9, 9, 9, 9, 9, 9,
         9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9,
         9,10,10,10,10,10,10,10,10,10,10,10,10,10,10,10,
        10,10,10,10,10,10,10,10, 9,10,10, 5,10, 8, 9, 8,
         9
    };

    private static readonly int[] SecondCodeLengths5 =
    {
         8,10,11,11,11,12,11,11,12, 6,11,12,10, 5,12,12,
        12,12,12,12,12,13,13,14,13,13,12,13,12,13,12,15,
         4,10, 7, 9,11,11,10, 9, 6, 7, 8, 9, 6, 7, 6, 7,
         8, 7, 7, 8, 8, 8, 8, 8, 8, 9, 8, 7,10, 9,10,10,
        11, 7, 8, 6, 7, 8, 8, 9, 8, 7,10,10, 8, 7, 8, 8,
         7,10, 7, 6, 7, 9, 9, 8,11,11,11,10,11,11,11, 8,
        11, 6, 7, 6, 6, 6, 6, 8, 7, 6,10, 9, 6, 7, 6, 6,
         7,10, 6, 5, 6, 7, 7, 7,10, 8,11, 9,13, 7,14,16,
        12,14,14,15,15,16,16,14,15,15,15,15,15,15,15,15,
        14,15,13,14,14,16,15,17,14,17,15,17,12,14,13,16,
        12,17,13,17,14,13,13,14,14,12,13,15,15,14,15,17,
        14,17,15,14,15,16,12,16,15,14,15,16,15,16,17,17,
        15,15,17,17,13,14,15,15,13,12,16,16,17,14,15,16,
        15,15,13,13,15,13,16,17,15,17,17,17,16,17,14,17,
        14,16,15,17,15,15,14,17,15,17,15,16,15,15,16,16,
        14,17,17,15,15,16,15,17,15,14,16,16,16,16,16,12,
         4, 4, 5, 5, 6, 6, 6, 7, 7, 7, 8, 8, 8, 8, 9, 9,
         9, 9, 9,10,10,10,11,10,11,11,11,11,11,12,12,12,
        13,13,12,13,12,14,14,12,13,13,13,13,14,12,13,13,
        14,14,14,13,14,14,15,15,13,15,13,17,17,17, 9,17,
         7
    };

    private static readonly int[] OffsetCodeLengths5 = { 6, 7, 7, 6, 4, 3, 2, 2, 3, 3, 6 };
}
