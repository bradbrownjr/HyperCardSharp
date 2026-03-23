using HyperCardSharp.Core.Binary;

namespace HyperCardSharp.Core.Stack;

/// <summary>
/// Parsed STBL block. Contains text styling information referenced by part content style runs.
/// Layout (after 16-byte header):
/// +0x10: styleCount(4), +0x14: nextStyleId(4),
/// +0x18: styles[] (each 24 bytes)
/// </summary>
public class StyleTableBlock
{
    public BlockHeader Header { get; init; }
    public int StyleCount { get; init; }
    public int NextStyleId { get; init; }
    public List<StyleEntry> Styles { get; init; } = new();

    public StyleEntry? GetStyle(int index)
        => index >= 0 && index < Styles.Count ? Styles[index] : null;

    public static StyleTableBlock Parse(ReadOnlySpan<byte> blockData, BlockHeader header)
    {
        var styleCount = BigEndianReader.ReadInt32At(blockData, 0x10);
        var nextStyleId = BigEndianReader.ReadInt32At(blockData, 0x14);

        var styles = new List<StyleEntry>();
        int offset = 0x18;

        for (int i = 0; i < styleCount; i++)
        {
            if (offset + 24 > blockData.Length)
                break;

            var styleNumber = BigEndianReader.ReadInt32At(blockData, offset);
            var runCount = BigEndianReader.ReadUInt16At(blockData, offset + 0x08);
            var textFontId = BigEndianReader.ReadInt16At(blockData, offset + 0x0C);
            var textStyle = (sbyte)blockData[offset + 0x0E];
            var textSize = BigEndianReader.ReadInt16At(blockData, offset + 0x10);

            styles.Add(new StyleEntry
            {
                StyleNumber = styleNumber,
                RunCount = runCount,
                TextFontId = textFontId,
                TextStyle = textStyle,
                TextSize = textSize
            });

            offset += 24;
        }

        return new StyleTableBlock
        {
            Header = header,
            StyleCount = styleCount,
            NextStyleId = nextStyleId,
            Styles = styles
        };
    }
}

public class StyleEntry
{
    public int StyleNumber { get; init; }
    public ushort RunCount { get; init; }

    /// <summary>Font ID from FTBL. -1 means inherit/no change.</summary>
    public short TextFontId { get; init; }

    /// <summary>Style bits (bold=1, italic=2, underline=4, outline=8, shadow=16, condense=32, extend=64, group=128). -1 means inherit.</summary>
    public sbyte TextStyle { get; init; }

    /// <summary>Font size in points. -1 means inherit/no change.</summary>
    public short TextSize { get; init; }

    public bool InheritFont => TextFontId == -1;
    public bool InheritStyle => TextStyle == -1;
    public bool InheritSize => TextSize == -1;
}
