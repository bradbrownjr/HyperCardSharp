using HyperCardSharp.Core.Binary;
using HyperCardSharp.Core.Parts;
using HyperCardSharp.Core.Stack;
using SkiaSharp;

namespace HyperCardSharp.Rendering.Tests.Fixtures;

/// <summary>
/// Builds a reusable in-memory HyperCard stack that exercises every <see cref="PartStyle"/>
/// as a button (a normal + hilited pair side by side) plus five representative field
/// styles, without touching the binary parser at all -- the model objects
/// (<see cref="StackFile"/>, <see cref="BackgroundBlock"/>, <see cref="CardBlock"/>,
/// <see cref="Part"/>, <see cref="PartContent"/>) are constructed directly via their
/// init/set properties. <see cref="CardRenderer"/>/<see cref="PartRenderer"/> consume
/// this exactly as they would a stack parsed from a real file.
///
/// Intended to be reused by later golden-image / visual regression work (roadmap task
/// B7) so that both this task's manual verification and any future automated comparison
/// render the identical fixture.
/// </summary>
public static class PartShowcaseFixture
{
    /// <summary>Standard HyperCard classic Mac card size.</summary>
    public const int CardWidth = 512;

    /// <summary>Standard HyperCard classic Mac card size.</summary>
    public const int CardHeight = 342;

    /// <summary>
    /// All 12 button-capable <see cref="PartStyle"/> values, in the order they are laid
    /// out on the showcase card (row-major, 4 columns).
    /// </summary>
    public static readonly PartStyle[] AllButtonStyles =
    {
        PartStyle.Transparent, PartStyle.Opaque, PartStyle.Rectangle, PartStyle.RoundRect,
        PartStyle.Shadow, PartStyle.CheckBox, PartStyle.RadioButton, PartStyle.Scrolling,
        PartStyle.Standard, PartStyle.Default, PartStyle.Oval, PartStyle.Popup
    };

    /// <summary>
    /// Field styles shown beneath the button grid, paired with sample text long enough
    /// to give the Scrolling field real overflow content.
    /// </summary>
    public static readonly (PartStyle Style, string Text)[] FieldStyles =
    {
        (PartStyle.Transparent, "Transparent field text."),
        (PartStyle.Opaque,      "Opaque field text."),
        (PartStyle.Rectangle,   "Rectangle field text."),
        (PartStyle.Shadow,      "Shadow field text."),
        (PartStyle.Scrolling,   "Scrolling field.\r" + string.Join("\r", Enumerable.Range(1, 20).Select(n => $"Line {n}")))
    };

    private const int ButtonGridMarginX = 8;
    private const int ButtonGridMarginY = 8;
    private const int ButtonCellWidth = 124;
    private const int ButtonCellHeight = 56;
    private const int ButtonWidth = 56;
    private const int ButtonHeight = 40;
    private const int ButtonGap = 6;
    private const int ButtonGridCols = 4;

    private const int FieldTop = 180;
    private const int FieldHeight = 130;
    private const int FieldWidth = 95;
    private const int FieldGap = 5;

    /// <summary>
    /// Builds the showcase stack: one background, one card, 24 buttons (12 styles x
    /// normal/hilited), and 5 fields.
    /// </summary>
    public static StackFile BuildStack()
    {
        var parts = new List<Part>();
        short nextId = 1;

        for (int i = 0; i < AllButtonStyles.Length; i++)
        {
            var style = AllButtonStyles[i];
            int row = i / ButtonGridCols;
            int col = i % ButtonGridCols;
            int cellLeft = ButtonGridMarginX + col * ButtonCellWidth;
            int cellTop = ButtonGridMarginY + row * ButtonCellHeight;

            // Popup gets a non-zero title width so its divider/label region actually renders.
            short titleWidth = style == PartStyle.Popup ? (short)30 : (short)0;

            parts.Add(MakeButton(nextId++, style, cellLeft, cellTop, ButtonWidth, ButtonHeight,
                style.ToString(), hilite: false, titleWidth));
            parts.Add(MakeButton(nextId++, style, cellLeft + ButtonWidth + ButtonGap, cellTop,
                ButtonWidth, ButtonHeight, style.ToString(), hilite: true, titleWidth));
        }

        var fieldContents = new List<PartContent>();
        for (int i = 0; i < FieldStyles.Length; i++)
        {
            short id = nextId++;
            int left = ButtonGridMarginX + i * (FieldWidth + FieldGap);
            var (style, text) = FieldStyles[i];
            parts.Add(MakeField(id, style, left, FieldTop, FieldWidth, FieldHeight, style.ToString()));
            fieldContents.Add(MakeContent(id, text));
        }

        var bg = new BackgroundBlock
        {
            Header = new BlockHeader { Type = "BKGD", Id = 1, Size = 0, FileOffset = 0 },
            BitmapId = 0,
            Flags = 0,
            Name = "PartShowcase Background",
            Script = ""
        };

        var card = new CardBlock
        {
            Header = new BlockHeader { Type = "CARD", Id = 1, Size = 0, FileOffset = 0 },
            BitmapId = 0,
            Flags = 0,
            BackgroundId = bg.Header.Id,
            Parts = parts,
            PartContents = fieldContents,
            Name = "PartStyle Showcase",
            Script = ""
        };

        var stackHeader = new StackBlock
        {
            Header = new BlockHeader { Type = "STAK", Id = -1, Size = 0, FileOffset = 0 },
            FormatVersion = 10,
            CardWidth = CardWidth,
            CardHeight = CardHeight
        };

        return new StackFile
        {
            StackHeader = stackHeader,
            Blocks = new List<BlockHeader>(),
            RawData = ReadOnlyMemory<byte>.Empty,
            Cards = new List<CardBlock> { card },
            Backgrounds = new List<BackgroundBlock> { bg }
        };
    }

    /// <summary>
    /// Convenience wrapper: builds the stack and renders its single showcase card to an
    /// SKBitmap via <see cref="CardRenderer"/>, exactly as the app would. Caller owns
    /// disposal of the returned bitmap.
    /// </summary>
    public static SKBitmap RenderShowcaseCard(RenderMode mode = RenderMode.BlackAndWhite)
    {
        var stack = BuildStack();
        var renderer = new CardRenderer(stack);
        return renderer.RenderCard(stack.Cards[0], mode);
    }

    private static Part MakeButton(short id, PartStyle style, int left, int top, int width, int height,
        string name, bool hilite, short titleWidth)
    {
        return new Part
        {
            PartId = id,
            Type = PartType.Button,
            Flags = 0x00,          // visible
            Top = (ushort)top,
            Left = (ushort)left,
            Bottom = (ushort)(top + height),
            Right = (ushort)(left + width),
            MoreFlags = 0x80,      // bit 7 = showName
            Style = style,
            TitleWidthOrLastSelectedLine = titleWidth,
            IconIdOrFirstSelectedLine = 0,
            TextAlign = 1,
            TextFontId = 4,
            TextSize = 9,
            TextStyle = 0,
            TextHeight = 12,
            Name = name,
            Script = "",
            HiliteState = hilite
        };
    }

    private static Part MakeField(short id, PartStyle style, int left, int top, int width, int height, string name)
    {
        return new Part
        {
            PartId = id,
            Type = PartType.Field,
            Flags = 0x00,          // visible
            Top = (ushort)top,
            Left = (ushort)left,
            Bottom = (ushort)(top + height),
            Right = (ushort)(left + width),
            MoreFlags = 0x00,
            Style = style,
            TitleWidthOrLastSelectedLine = 0,
            IconIdOrFirstSelectedLine = 0,
            TextAlign = 0,
            TextFontId = 4,
            TextSize = 10,
            TextStyle = 0,
            TextHeight = 12,
            Name = name,
            Script = ""
        };
    }

    private static PartContent MakeContent(short fieldPartId, string text)
        => new() { PartId = (short)(-fieldPartId), Text = text };
}
