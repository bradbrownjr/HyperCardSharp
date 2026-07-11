using System.Text;
using HyperCardSharp.Core.Binary;
using HyperCardSharp.Core.Parts;
using HyperCardSharp.Core.Stack;
using SkiaSharp;

namespace HyperCardSharp.Rendering.Tests.Fixtures;

/// <summary>
/// Builds a small synthetic in-memory HyperCard stack whose single field carries
/// STBL-style styled text runs (mixed font size + bold/italic/bold-italic spans),
/// exercising the <see cref="TextRenderer"/> styled-run path (finding F8) at the
/// pixel level. Built directly from the model objects (<see cref="StackFile"/>,
/// <see cref="StyleTableBlock"/>, <see cref="PartContent.StyleRuns"/>) exactly like
/// <see cref="PartShowcaseFixture"/>, so no binary parsing or sample stack is involved.
///
/// The base part font is Mac font ID 0 ("Chicago"), which resolves to the embedded
/// ChicagoFLF substitute once <see cref="FontMapper.UseEmbeddedFontsOnly"/> is set,
/// keeping this fixture's golden-image rendering portable (roadmap task B7).
/// </summary>
public static class StyledTextFixture
{
    /// <summary>Standard HyperCard classic Mac card size.</summary>
    public const int CardWidth = 512;

    /// <summary>Standard HyperCard classic Mac card size.</summary>
    public const int CardHeight = 342;

    private const int FieldLeft = 20;
    private const int FieldTop = 20;
    private const int FieldRight = 492;
    private const int FieldBottom = 300;

    // Mac font ID 0 = "Chicago" (see FontMapper.MacFontNames); with UseEmbeddedFontsOnly
    // this always resolves to the embedded ChicagoFLF substitute.
    private const short ChicagoFontId = 0;

    /// <summary>
    /// Word/style pairs making up the field text, in reading order. Character offsets
    /// for the style runs are computed from these at build time so they always line up.
    /// </summary>
    private static readonly (string Text, int StyleId)[] Words =
    {
        ("Plain ",       0),
        ("Bold ",        1),
        ("Italic ",      2),
        ("BoldItalic ",  3),
        ("Large ",       4),
        ("Underline",    5),
    };

    /// <summary>
    /// Builds the styled-text stack: one background, one card, one field whose
    /// content carries six style runs (plain, bold, italic, bold+italic, large plain,
    /// underline).
    /// </summary>
    public static StackFile BuildStack()
    {
        var field = new Part
        {
            PartId = 1,
            Type = PartType.Field,
            Flags = 0x00,           // visible
            Top = FieldTop,
            Left = FieldLeft,
            Bottom = FieldBottom,
            Right = FieldRight,
            MoreFlags = 0x00,
            Style = PartStyle.Rectangle,
            TitleWidthOrLastSelectedLine = 0,
            IconIdOrFirstSelectedLine = 0,
            TextAlign = 0,
            TextFontId = ChicagoFontId,
            TextSize = 14,
            TextStyle = 0,
            TextHeight = 18,
            Name = "StyledField",
            Script = ""
        };

        var sb = new StringBuilder();
        var styleRuns = new List<StyleRun>();
        foreach (var (text, styleId) in Words)
        {
            styleRuns.Add(new StyleRun { CharacterPosition = (ushort)sb.Length, StyleId = (ushort)styleId });
            sb.Append(text);
        }

        var content = new PartContent
        {
            PartId = (short)(-field.PartId),
            Text = sb.ToString(),
            StyleRuns = styleRuns
        };

        // All entries inherit font (Chicago) and only vary style bits / size, so the
        // fixture stays anchored to the embedded ChicagoFLF substitute in every run.
        var styleTable = new StyleTableBlock
        {
            Header = new BlockHeader { Type = "STBL", Id = 1, Size = 0, FileOffset = 0 },
            StyleCount = 6,
            NextStyleId = 6,
            Styles = new List<StyleEntry>
            {
                new() { StyleNumber = 0, RunCount = 1, TextFontId = -1, TextStyle = 0b00000, TextSize = -1 }, // plain
                new() { StyleNumber = 1, RunCount = 1, TextFontId = -1, TextStyle = 0b00001, TextSize = -1 }, // bold
                new() { StyleNumber = 2, RunCount = 1, TextFontId = -1, TextStyle = 0b00010, TextSize = -1 }, // italic
                new() { StyleNumber = 3, RunCount = 1, TextFontId = -1, TextStyle = 0b00011, TextSize = -1 }, // bold+italic
                new() { StyleNumber = 4, RunCount = 1, TextFontId = -1, TextStyle = 0b00000, TextSize = 24 }, // large plain
                new() { StyleNumber = 5, RunCount = 1, TextFontId = -1, TextStyle = 0b00100, TextSize = -1 }, // underline
            }
        };

        var bg = new BackgroundBlock
        {
            Header = new BlockHeader { Type = "BKGD", Id = 1, Size = 0, FileOffset = 0 },
            BitmapId = 0,
            Flags = 0,
            Name = "StyledText Background",
            Script = ""
        };

        var card = new CardBlock
        {
            Header = new BlockHeader { Type = "CARD", Id = 1, Size = 0, FileOffset = 0 },
            BitmapId = 0,
            Flags = 0,
            BackgroundId = bg.Header.Id,
            Parts = new List<Part> { field },
            PartContents = new List<PartContent> { content },
            Name = "Styled Text Showcase",
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
            StyleTable = styleTable,
            Blocks = new List<BlockHeader>(),
            RawData = ReadOnlyMemory<byte>.Empty,
            Cards = new List<CardBlock> { card },
            Backgrounds = new List<BackgroundBlock> { bg }
        };
    }

    /// <summary>
    /// Convenience wrapper: builds the stack and renders its single card to an
    /// SKBitmap via <see cref="CardRenderer"/>. Caller owns disposal of the returned bitmap.
    /// </summary>
    public static SKBitmap RenderStyledTextCard(RenderMode mode = RenderMode.BlackAndWhite)
    {
        var stack = BuildStack();
        var renderer = new CardRenderer(stack);
        return renderer.RenderCard(stack.Cards[0], mode);
    }
}
