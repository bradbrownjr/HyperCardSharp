using HyperCardSharp.Core.Binary;
using HyperCardSharp.Core.Text;

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
    /// <summary>Top coordinate of the part rect. Settable by HyperTalk.</summary>
    public ushort Top { get; set; }
    /// <summary>Left coordinate of the part rect. Settable by HyperTalk.</summary>
    public ushort Left { get; set; }
    /// <summary>Bottom coordinate of the part rect. Settable by HyperTalk.</summary>
    public ushort Bottom { get; set; }
    /// <summary>Right coordinate of the part rect. Settable by HyperTalk.</summary>
    public ushort Right { get; set; }
    public byte MoreFlags { get; init; }
    /// <summary>Part style (transparent, opaque, shadow, etc.). Settable by HyperTalk.</summary>
    public PartStyle Style { get; set; }
    public short TitleWidthOrLastSelectedLine { get; init; }
    public short IconIdOrFirstSelectedLine { get; init; }
    public short TextAlign { get; init; }
    /// <summary>Mac font ID. Settable so HyperTalk can change it at runtime.</summary>
    public short TextFontId { get; set; }
    /// <summary>Text point size. Settable so HyperTalk can change it at runtime.</summary>
    public ushort TextSize { get; set; }
    /// <summary>Text style flags (bold=1, italic=2, underline=4 …). Settable by HyperTalk.</summary>
    public byte TextStyle { get; set; }
    public ushort TextHeight { get; init; }
    /// <summary>Part name. Settable so HyperTalk can rename a part at runtime.</summary>
    public string Name { get; set; } = "";
    public string Script { get; init; } = "";

    // ── Runtime-mutable state (overrides parsed values during script execution) ──

    /// <summary>
    /// Runtime visibility override.  <c>null</c> means use the parsed flag;
    /// <c>true</c>/<c>false</c> was set by a HyperTalk <c>show</c>/<c>hide</c> command.
    /// </summary>
    public bool? VisibleOverride { get; set; }

    /// <summary>Runtime hilite/highlight state for buttons (toggled by HyperTalk).</summary>
    public bool HiliteState { get; set; }

    /// <summary>
    /// Runtime enabled override.  <c>null</c> means enabled (default);
    /// <c>false</c> was set by <c>set the enabled of button X to false</c>.
    /// </summary>
    public bool? EnabledOverride { get; set; }

    /// <summary>True unless <see cref="EnabledOverride"/> has been set to <c>false</c>.</summary>
    public bool Enabled => EnabledOverride ?? true;
    // ── Scroll state (runtime-only; not stored in the binary format) ──────────

    /// <summary>
    /// Current vertical scroll offset in pixels (0 = scrolled to top).
    /// Updated when the user clicks the scrollbar arrows or track.
    /// Clamped to [0, <see cref="MaxScrollY"/>].
    /// </summary>
    public float ScrollOffsetY { get; set; } = 0f;

    /// <summary>
    /// Maximum useful scroll offset in pixels (= total content height − field height).
    /// Recomputed by <see cref="TextRenderer"/> after each render pass.
    /// 0 means the content fits entirely within the field — no scrolling needed.
    /// </summary>
    public float MaxScrollY { get; set; } = 0f;
    // Convenience properties
    public bool IsButton => Type == PartType.Button;
    public bool IsField => Type == PartType.Field;
    public bool Visible => VisibleOverride ?? ((Flags & 0x80) == 0); // override wins; bit 7 inverted = ~visible
    /// <summary>For buttons: bit 7 of MoreFlags = showName (display the button label).</summary>
    public bool ShowName => IsButton && (MoreFlags & 0x80) != 0;
    /// <summary>For buttons: the icon resource ID (0 = no icon).</summary>
    public short IconId => IsButton ? IconIdOrFirstSelectedLine : (short)0;

    // ── Additional part flag accessors (Flags / MoreFlags bit layout) ──────────
    //
    // Verified against HyperCardPreview's FilePart.swift (Pierre Lorenzi, GPL) as a
    // format SPECIFICATION only -- no code from that project was copied, only the
    // documented bit offsets/meanings, which are also written up in
    // docs/stack-format.md under "Part Record flag bits".
    //
    // Flags (offset 0x05) bit 0 is a SHARED bit whose meaning depends on part type:
    // for fields it is "lockText"; for buttons the same bit (inverted) is "enabled".
    // This codebase does not currently wire the button side into the runtime
    // Enabled/EnabledOverride properties (those remain purely a HyperTalk runtime
    // concept) -- only the field-side LockText accessor is exposed here since it is
    // the one requested by the B2 part-style audit.

    /// <summary>For fields: bit 0 (0x01) of <see cref="Flags"/> = lockText (the field's
    /// text cannot be edited directly by the user, though scripts may still change it).
    /// This bit is shared with the button "enabled" flag (inverted) at the same position.</summary>
    public bool LockText => IsField && (Flags & 0x01) != 0;

    /// <summary>For fields: bit 3 (0x08) of <see cref="Flags"/> = sharedText (the field's
    /// text content is shared across every card that uses this background, rather than
    /// each card having its own copy).</summary>
    public bool SharedText => IsField && (Flags & 0x08) != 0;

    /// <summary>For buttons: bit 6 (0x40) of <see cref="MoreFlags"/> = the persisted
    /// initial hilite state as authored in the stack file. Used only to seed
    /// <see cref="HiliteState"/> at parse time; runtime hilite changes are tracked by
    /// <see cref="HiliteState"/> itself, not this property.</summary>
    public bool FileHilite => IsButton && (MoreFlags & 0x40) != 0;

    /// <summary>For buttons: bit 5 (0x20) of <see cref="MoreFlags"/> = autoHilite
    /// (the button automatically inverts/hilites itself while the mouse is down over it,
    /// without needing an explicit "set the hilite" script command).</summary>
    public bool AutoHilite => IsButton && (MoreFlags & 0x20) != 0;

    /// <summary>For buttons: bit 4 (0x10) of <see cref="MoreFlags"/>, STORED INVERTED =
    /// sharedHilite (whether the hilite state is shared across every card that uses this
    /// background part, vs. each card tracking its own hilite independently).</summary>
    public bool SharedHilite => IsButton && (MoreFlags & 0x10) == 0;

    /// <summary>For fields: bit 6 (0x40) of <see cref="MoreFlags"/> = showLines
    /// (draw a ruled line under each row of text).</summary>
    public bool ShowLines => IsField && (MoreFlags & 0x40) != 0;

    /// <summary>For fields: bit 5 (0x20) of <see cref="MoreFlags"/> = wideMargins
    /// (extra left/right text inset).</summary>
    public bool WideMargins => IsField && (MoreFlags & 0x20) != 0;

    /// <summary>For buttons: low nibble (0x0F) of <see cref="MoreFlags"/> = the button
    /// "family" number (0-15). Radio buttons sharing a non-zero family number act as a
    /// mutually-exclusive group (hiliting one un-hilites the others in the same family).</summary>
    public int Family => IsButton ? (MoreFlags & 0x0F) : 0;
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

        var part = new Part
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

        // Seed the runtime hilite state from the persisted file bit (MoreFlags bit 6 for
        // buttons) so checkboxes/radio buttons authored as initially-checked render that
        // way on first load. HyperTalk scripts remain free to change HiliteState afterward.
        part.HiliteState = part.FileHilite;

        return part;
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

        return MacRomanEncoding.GetString(data.Slice(offset, end - offset));
    }
}
