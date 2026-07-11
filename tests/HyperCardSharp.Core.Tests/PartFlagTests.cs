using HyperCardSharp.Core.Parts;

namespace HyperCardSharp.Core.Tests;

/// <summary>
/// Fixture-byte tests for <see cref="Part"/> flag accessors. Each test builds a minimal
/// 30-byte part record (no name/script) and asserts the accessor reads the expected bit,
/// per the layout documented in docs/stack-format.md ("Part Record flag bits").
///
/// Byte layout used by these fixtures (all fields big-endian):
///   0x00 UInt16 entrySize   0x02 Int16 partId   0x04 Byte partType (low nibble = 1/2)
///   0x05 Byte flags         0x06-0x0D rect (4 x UInt16)
///   0x0E Byte moreFlags     0x0F Byte style     0x10-0x1D remaining fixed fields
/// </summary>
public class PartFlagTests
{
    private const int EntrySize = 30;

    /// <summary>Builds a minimal 30-byte part record with the given type/flags/moreFlags/style.</summary>
    private static byte[] BuildPartBytes(byte partType, byte flags, byte moreFlags, byte style = 0)
    {
        var data = new byte[EntrySize];
        // 0x00-0x01: entrySize
        data[0] = 0x00; data[1] = (byte)EntrySize;
        // 0x02-0x03: partId = 1
        data[2] = 0x00; data[3] = 0x01;
        data[4] = partType;
        data[5] = flags;
        // 0x06-0x0D: rect (top, left, bottom, right) = 0,0,10,20 -- non-zero size for IsButton/IsField checks
        data[6] = 0x00; data[7] = 0x00;   // top
        data[8] = 0x00; data[9] = 0x00;   // left
        data[10] = 0x00; data[11] = 0x0A; // bottom = 10
        data[12] = 0x00; data[13] = 0x14; // right = 20
        data[14] = moreFlags;
        data[15] = style;
        // 0x10-0x1D: titleWidth, iconId, textAlign, textFontId, textSize, textStyle, pad, textHeight -- all zero
        return data;
    }

    private static Part ParseButton(byte flags, byte moreFlags, byte style = 0)
        => Part.Parse(BuildPartBytes(partType: 1, flags: flags, moreFlags: moreFlags, style: style));

    private static Part ParseField(byte flags, byte moreFlags, byte style = 0)
        => Part.Parse(BuildPartBytes(partType: 2, flags: flags, moreFlags: moreFlags, style: style));

    // ── Hidden / Visible (Flags bit 7, 0x80) ────────────────────────────────

    [Fact]
    public void Visible_WhenHiddenBitClear_IsTrue()
    {
        var part = ParseButton(flags: 0x00, moreFlags: 0x00);
        Assert.True(part.Visible);
    }

    [Fact]
    public void Visible_WhenHiddenBitSet_IsFalse()
    {
        var part = ParseButton(flags: 0x80, moreFlags: 0x00);
        Assert.False(part.Visible);
    }

    // ── LockText (field) / shared bit 0x01 with button "enabled" ───────────

    [Fact]
    public void LockText_FieldWithBitSet_IsTrue()
    {
        var part = ParseField(flags: 0x01, moreFlags: 0x00);
        Assert.True(part.LockText);
    }

    [Fact]
    public void LockText_FieldWithBitClear_IsFalse()
    {
        var part = ParseField(flags: 0x00, moreFlags: 0x00);
        Assert.False(part.LockText);
    }

    [Fact]
    public void LockText_ButtonType_IsAlwaysFalse()
    {
        // Same bit position (0x01) means "enabled" (inverted) for buttons, not lockText.
        var part = ParseButton(flags: 0x01, moreFlags: 0x00);
        Assert.False(part.LockText);
    }

    // ── SharedText (field, Flags bit 3, 0x08) ───────────────────────────────

    [Fact]
    public void SharedText_FieldWithBitSet_IsTrue()
    {
        var part = ParseField(flags: 0x08, moreFlags: 0x00);
        Assert.True(part.SharedText);
    }

    [Fact]
    public void SharedText_FieldWithBitClear_IsFalse()
    {
        var part = ParseField(flags: 0x00, moreFlags: 0x00);
        Assert.False(part.SharedText);
    }

    // ── ShowName (button, MoreFlags bit 7, 0x80) ────────────────────────────

    [Fact]
    public void ShowName_ButtonWithBitSet_IsTrue()
    {
        var part = ParseButton(flags: 0x00, moreFlags: 0x80);
        Assert.True(part.ShowName);
    }

    [Fact]
    public void ShowName_FieldType_IsAlwaysFalse()
    {
        var part = ParseField(flags: 0x00, moreFlags: 0x80);
        Assert.False(part.ShowName);
    }

    // ── FileHilite (button, MoreFlags bit 6, 0x40) ──────────────────────────

    [Fact]
    public void FileHilite_ButtonWithBitSet_IsTrue()
    {
        var part = ParseButton(flags: 0x00, moreFlags: 0x40);
        Assert.True(part.FileHilite);
    }

    [Fact]
    public void FileHilite_ButtonWithBitClear_IsFalse()
    {
        var part = ParseButton(flags: 0x00, moreFlags: 0x00);
        Assert.False(part.FileHilite);
    }

    [Fact]
    public void HiliteState_SeededFromFileHilite_OnParse()
    {
        var hilited = ParseButton(flags: 0x00, moreFlags: 0x40);
        Assert.True(hilited.HiliteState);

        var notHilited = ParseButton(flags: 0x00, moreFlags: 0x00);
        Assert.False(notHilited.HiliteState);
    }

    // ── AutoHilite (button, MoreFlags bit 5, 0x20) ──────────────────────────

    [Fact]
    public void AutoHilite_ButtonWithBitSet_IsTrue()
    {
        var part = ParseButton(flags: 0x00, moreFlags: 0x20);
        Assert.True(part.AutoHilite);
    }

    [Fact]
    public void AutoHilite_ButtonWithBitClear_IsFalse()
    {
        var part = ParseButton(flags: 0x00, moreFlags: 0x00);
        Assert.False(part.AutoHilite);
    }

    // ── SharedHilite (button, MoreFlags bit 4, 0x10, STORED INVERTED) ───────

    [Fact]
    public void SharedHilite_ButtonWithBitClear_IsTrue()
    {
        var part = ParseButton(flags: 0x00, moreFlags: 0x00);
        Assert.True(part.SharedHilite);
    }

    [Fact]
    public void SharedHilite_ButtonWithBitSet_IsFalse()
    {
        var part = ParseButton(flags: 0x00, moreFlags: 0x10);
        Assert.False(part.SharedHilite);
    }

    // ── ShowLines (field, MoreFlags bit 6, 0x40 -- same bit as button FileHilite) ──

    [Fact]
    public void ShowLines_FieldWithBitSet_IsTrue()
    {
        var part = ParseField(flags: 0x00, moreFlags: 0x40);
        Assert.True(part.ShowLines);
    }

    [Fact]
    public void ShowLines_ButtonType_IsAlwaysFalse()
    {
        var part = ParseButton(flags: 0x00, moreFlags: 0x40);
        Assert.False(part.ShowLines);
    }

    // ── WideMargins (field, MoreFlags bit 5, 0x20 -- same bit as button AutoHilite) ─

    [Fact]
    public void WideMargins_FieldWithBitSet_IsTrue()
    {
        var part = ParseField(flags: 0x00, moreFlags: 0x20);
        Assert.True(part.WideMargins);
    }

    [Fact]
    public void WideMargins_FieldWithBitClear_IsFalse()
    {
        var part = ParseField(flags: 0x00, moreFlags: 0x00);
        Assert.False(part.WideMargins);
    }

    // ── Family (button, MoreFlags low nibble 0x0F) ──────────────────────────

    [Theory]
    [InlineData(0x00, 0)]
    [InlineData(0x05, 5)]
    [InlineData(0x0F, 15)]
    [InlineData(0xF5, 5)] // high nibble (hilite/autoHilite/sharedHilite/showName bits) must not leak into family
    public void Family_ButtonMasksLowNibbleOnly(byte moreFlags, int expectedFamily)
    {
        var part = ParseButton(flags: 0x00, moreFlags: moreFlags);
        Assert.Equal(expectedFamily, part.Family);
    }

    [Fact]
    public void Family_FieldType_IsAlwaysZero()
    {
        var part = ParseField(flags: 0x00, moreFlags: 0x0F);
        Assert.Equal(0, part.Family);
    }
}
