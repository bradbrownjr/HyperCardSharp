using HyperCardSharp.Core.Binary;

namespace HyperCardSharp.Core.Parts;

/// <summary>
/// A button or field within a CARD or BKGD block.
/// </summary>
public class Part
{
    public int EntrySize { get; init; }
    public short PartId { get; init; }
    public PartType Type { get; init; }
    public byte Flags { get; init; }
    public ushort Top { get; init; }
    public ushort Left { get; init; }
    public ushort Bottom { get; init; }
    public ushort Right { get; init; }
    public byte MoreFlags { get; init; }
    public PartStyle Style { get; init; }
    public short TitleWidthOrLastSelectedLine { get; init; }
    public short IconIdOrFirstSelectedLine { get; init; }
    public short TextAlign { get; init; }
    public short TextFontId { get; init; }
    public ushort TextSize { get; init; }
    public byte TextStyle { get; init; }
    public ushort TextHeight { get; init; }
    public string Name { get; init; } = "";
    public string Script { get; init; } = "";

    // Convenience properties
    public bool IsButton => Type == PartType.Button;
    public bool IsField => Type == PartType.Field;
    public bool Visible => (Flags & 0x80) == 0; // bit 7 inverted = ~visible
    /// <summary>For buttons: bit 7 of MoreFlags = showName (display the button label).</summary>
    public bool ShowName => IsButton && (MoreFlags & 0x80) != 0;
    /// <summary>For buttons: the icon resource ID (0 = no icon).</summary>
    public short IconId => IsButton ? IconIdOrFirstSelectedLine : (short)0;
    public int Width => Right - Left;
    public int Height => Bottom - Top;

    /// <summary>
    /// Parse a single part entry from CARD/BKGD part data.
    /// </summary>
    public static Part Parse(ReadOnlySpan<byte> data)
    {
        var reader = new BigEndianReader(data);
        var entrySize = reader.ReadUInt16();
        var partId = reader.ReadInt16();
        var partType = reader.ReadByte();
        var flags = reader.ReadByte();
        var top = reader.ReadUInt16();
        var left = reader.ReadUInt16();
        var bottom = reader.ReadUInt16();
        var right = reader.ReadUInt16();
        var moreFlags = reader.ReadByte();
        var style = reader.ReadByte();
        var titleWidth = reader.ReadInt16();
        var iconId = reader.ReadInt16();
        var textAlign = reader.ReadInt16();
        var textFontId = reader.ReadInt16();
        var textSize = reader.ReadUInt16();
        var textStyle = reader.ReadByte();
        reader.Skip(1); // padding byte at +0x1B
        var textHeight = reader.ReadUInt16();

        // +0x1E: name (null-terminated), then separator 0x00, then script (null-terminated)
        var name = ReadNullTerminatedString(data, reader.Offset);
        var nameEndOffset = reader.Offset + name.Length + 1; // +1 for null terminator

        var script = "";
        if (nameEndOffset < entrySize)
        {
            // Skip separator byte
            var scriptOffset = nameEndOffset + 1;
            if (scriptOffset < entrySize)
                script = ReadNullTerminatedString(data, scriptOffset);
        }

        return new Part
        {
            EntrySize = entrySize,
            PartId = partId,
            Type = (PartType)(partType & 0x0F), // low nibble only
            Flags = flags,
            Top = top,
            Left = left,
            Bottom = bottom,
            Right = right,
            MoreFlags = moreFlags,
            Style = (PartStyle)style,
            TitleWidthOrLastSelectedLine = titleWidth,
            IconIdOrFirstSelectedLine = iconId,
            TextAlign = textAlign,
            TextFontId = textFontId,
            TextSize = textSize,
            TextStyle = textStyle,
            TextHeight = textHeight,
            Name = name,
            Script = script
        };
    }

    /// <summary>
    /// Parse all parts from a CARD/BKGD block's part list region.
    /// </summary>
    public static List<Part> ParseAll(ReadOnlySpan<byte> partListData, int partCount)
    {
        var parts = new List<Part>(partCount);
        int offset = 0;

        for (int i = 0; i < partCount; i++)
        {
            if (offset + 2 > partListData.Length)
                break;

            var partData = partListData.Slice(offset);
            var part = Parse(partData);
            parts.Add(part);
            offset += part.EntrySize;
        }

        return parts;
    }

    private static string ReadNullTerminatedString(ReadOnlySpan<byte> data, int offset)
    {
        int end = offset;
        while (end < data.Length && data[end] != 0)
            end++;

        if (end == offset)
            return "";

        return System.Text.Encoding.Latin1.GetString(data.Slice(offset, end - offset));
    }
}
