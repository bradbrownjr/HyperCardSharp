using HyperCardSharp.Core.Binary;
using HyperCardSharp.Core.Text;

namespace HyperCardSharp.Core.Stack;

/// <summary>
/// Parsed STAK block — the stack header containing global metadata.
/// Based on HC 2.x format (version 10).
///
/// Key offsets within the block (after the 16-byte header at +0x00):
///   +0x10: format version (4 bytes) — 10 for HC 2.x
///   ...
///   +0x1BC: stack-level script (null-terminated Mac Roman string)
///   +0x14: total size of stack data (4 bytes)
///   +0x18: size of STAK block (4 bytes, typically 0x800 = 2048)
///   +0x1C: (unknown/reserved)
///   +0x20: number of backgrounds (4 bytes)
///   +0x24: first background ID (4 bytes)
///   +0x28: number of cards (4 bytes)
///   +0x2C: first card ID (4 bytes)
///   +0x30: LIST block ID (4 bytes)
///   +0x34: FREE block count (4 bytes)
///   +0x38: FREE block total size (4 bytes)
///   +0x3C: PRNT block ID (4 bytes)
///   +0x40: password hash (4 bytes)
///   +0x44: user level (2 bytes)
///   +0x48: protection flags (2 bytes)
///   +0x60+: 40 patterns (8 bytes each = 320 bytes)
///   +0x1B0: FTBL block ID (4 bytes)
///   +0x1B4: STBL block ID (4 bytes)
///   +0x1B8: card window height (2 bytes)
///   +0x1BA: card window width (2 bytes)
/// </summary>
public class StackBlock
{
    public BlockHeader Header { get; init; }
    public int FormatVersion { get; init; }
    public int TotalStackSize { get; init; }
    public int StackBlockSize { get; init; }
    public int BackgroundCount { get; init; }
    public int FirstBackgroundId { get; init; }
    public int CardCount { get; init; }
    public int FirstCardId { get; init; }
    public int ListBlockId { get; init; }
    public int FreeBlockCount { get; init; }
    public int FreeBlockSize { get; init; }
    public int PrintBlockId { get; init; }
    public uint PasswordHash { get; init; }
    public short UserLevel { get; init; }
    public short ProtectionFlags { get; init; }
    public int FontTableId { get; init; }
    public int StyleTableId { get; init; }
    public short CardHeight { get; init; }
    public short CardWidth { get; init; }
    public byte[][] Patterns { get; init; } = Array.Empty<byte[]>();
    /// <summary>Stack-level HyperTalk script (null-terminated Mac Roman string at +0x1BC).</summary>
    public string Script { get; init; } = "";

    public static StackBlock Parse(ReadOnlySpan<byte> blockData, BlockHeader header)
    {
        var reader = new BigEndianReader(blockData);

        // Skip past the 16-byte header (already parsed)
        reader.Seek(0x10);

        var formatVersion = reader.ReadInt32();      // +0x10
        var totalStackSize = reader.ReadInt32();      // +0x14
        var stackBlockSize = reader.ReadInt32();      // +0x18
        reader.Skip(4);                               // +0x1C reserved

        var backgroundCount = reader.ReadInt32();     // +0x20
        var firstBackgroundId = reader.ReadInt32();    // +0x24
        var cardCount = reader.ReadInt32();            // +0x28
        var firstCardId = reader.ReadInt32();          // +0x2C
        var listBlockId = reader.ReadInt32();          // +0x30
        var freeBlockCount = reader.ReadInt32();       // +0x34
        var freeBlockSize = reader.ReadInt32();        // +0x38
        var printBlockId = reader.ReadInt32();         // +0x3C
        var passwordHash = reader.ReadUInt32();        // +0x40
        var userLevel = reader.ReadInt16();            // +0x44
        reader.Skip(2);                               // +0x46 padding
        var protectionFlags = reader.ReadInt16();      // +0x48

        // Read 40 patterns starting at +0x60 (8 bytes each, 320 bytes total)
        reader.Seek(0x60);
        var patterns = new byte[40][];
        for (int i = 0; i < 40; i++)
        {
            patterns[i] = reader.ReadBytes(8).ToArray();
        }

        // Font and style table IDs, card dimensions
        reader.Seek(0x1B0);
        var fontTableId = reader.ReadInt32();          // +0x1B0
        var styleTableId = reader.ReadInt32();         // +0x1B4
        var cardHeight = reader.ReadInt16();           // +0x1B8
        var cardWidth = reader.ReadInt16();            // +0x1BA

        // Stack-level script: null-terminated Mac Roman string at +0x1BC
        string script = "";
        if (blockData.Length > 0x1BC)
            script = ReadNullTerminatedString(blockData, 0x1BC);

        return new StackBlock
        {
            Header = header,
            FormatVersion = formatVersion,
            TotalStackSize = totalStackSize,
            StackBlockSize = stackBlockSize,
            BackgroundCount = backgroundCount,
            FirstBackgroundId = firstBackgroundId,
            CardCount = cardCount,
            FirstCardId = firstCardId,
            ListBlockId = listBlockId,
            FreeBlockCount = freeBlockCount,
            FreeBlockSize = freeBlockSize,
            PrintBlockId = printBlockId,
            PasswordHash = passwordHash,
            UserLevel = userLevel,
            ProtectionFlags = protectionFlags,
            FontTableId = fontTableId,
            StyleTableId = styleTableId,
            CardHeight = cardHeight,
            CardWidth = cardWidth,
            Patterns = patterns,
            Script = script
        };
    }

    private static string ReadNullTerminatedString(ReadOnlySpan<byte> data, int offset)
    {
        int end = offset;
        while (end < data.Length && data[end] != 0)
            end++;
        if (end == offset) return "";
        return MacRomanEncoding.GetString(data.Slice(offset, end - offset));
    }
}
