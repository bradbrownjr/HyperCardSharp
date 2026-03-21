using HyperCardSharp.Core.Binary;

namespace HyperCardSharp.Core.Tests;

public class BigEndianReaderTests
{
    [Fact]
    public void ReadInt32_ParsesBigEndian()
    {
        // 0x00001000 = 4096 in big-endian
        ReadOnlySpan<byte> data = new byte[] { 0x00, 0x00, 0x10, 0x00 };
        var reader = new BigEndianReader(data);
        Assert.Equal(4096, reader.ReadInt32());
    }

    [Fact]
    public void ReadInt16_ParsesBigEndian()
    {
        ReadOnlySpan<byte> data = new byte[] { 0x01, 0x00 };
        var reader = new BigEndianReader(data);
        Assert.Equal(256, reader.ReadInt16());
    }

    [Fact]
    public void ReadAscii_ParsesString()
    {
        ReadOnlySpan<byte> data = new byte[] { 0x53, 0x54, 0x41, 0x4B }; // "STAK"
        var reader = new BigEndianReader(data);
        Assert.Equal("STAK", reader.ReadAscii(4));
    }

    [Fact]
    public void ReadInt32_NegativeOne()
    {
        // 0xFFFFFFFF = -1 in signed int32
        ReadOnlySpan<byte> data = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };
        var reader = new BigEndianReader(data);
        Assert.Equal(-1, reader.ReadInt32());
    }

    [Fact]
    public void Seek_SetsOffset()
    {
        ReadOnlySpan<byte> data = new byte[] { 0x00, 0x00, 0x00, 0x42, 0x00, 0x00, 0x00, 0x43 };
        var reader = new BigEndianReader(data);
        reader.Seek(4);
        Assert.Equal(0x43, reader.ReadInt32());
    }

    [Fact]
    public void ReadInt32At_ReadsWithoutAdvancing()
    {
        byte[] data = { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x00 };
        Assert.Equal(0x0800, BigEndianReader.ReadInt32At(data, 4));
    }
}

public class BlockHeaderTests
{
    [Fact]
    public void Parse_StakHeader()
    {
        // Real STAK header bytes from NEUROBLAST
        byte[] data = {
            0x00, 0x00, 0x08, 0x00,  // size = 2048
            0x53, 0x54, 0x41, 0x4B,  // "STAK"
            0xFF, 0xFF, 0xFF, 0xFF,  // id = -1
            0x00, 0x00, 0x00, 0x00   // filler
        };

        var header = BlockHeader.Parse(data, 0);

        Assert.Equal(2048, header.Size);
        Assert.Equal("STAK", header.Type);
        Assert.Equal(-1, header.Id);
        Assert.Equal(0, header.FileOffset);
    }

    [Fact]
    public void Parse_MastHeader()
    {
        byte[] data = {
            0x00, 0x00, 0x04, 0x00,  // size = 1024
            0x4D, 0x41, 0x53, 0x54,  // "MAST"
            0xFF, 0xFF, 0xFF, 0xFF,  // id = -1
            0x00, 0x00, 0x00, 0x00   // filler
        };

        var header = BlockHeader.Parse(data, 0x800);

        Assert.Equal(1024, header.Size);
        Assert.Equal("MAST", header.Type);
        Assert.Equal(-1, header.Id);
        Assert.Equal(0x800, header.FileOffset);
    }
}

public class MagicDetectorTests
{
    [Fact]
    public void Detect_HyperCardStack()
    {
        // STAK magic at offset 4
        byte[] data = { 0x00, 0x00, 0x08, 0x00, 0x53, 0x54, 0x41, 0x4B };
        Assert.Equal(FileFormat.HyperCardStack, MagicDetector.Detect(data));
    }

    [Fact]
    public void Detect_StuffItArchive()
    {
        byte[] data = { 0x53, 0x49, 0x54, 0x21, 0x00, 0x01, 0x00, 0x0C };
        Assert.Equal(FileFormat.StuffItArchive, MagicDetector.Detect(data));
    }

    [Fact]
    public void Detect_AppleSingle()
    {
        byte[] data = { 0x00, 0x05, 0x16, 0x00, 0x00, 0x01, 0x00, 0x00 };
        Assert.Equal(FileFormat.AppleSingle, MagicDetector.Detect(data));
    }

    [Fact]
    public void Detect_Unknown()
    {
        byte[] data = { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(FileFormat.Unknown, MagicDetector.Detect(data));
    }
}
