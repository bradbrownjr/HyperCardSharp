using System.Buffers.Binary;
using System.Text;

namespace HyperCardSharp.Core.Binary;

/// <summary>
/// Zero-allocation big-endian binary reader over a ReadOnlySpan&lt;byte&gt;.
/// All HyperCard data is big-endian (Motorola 68000).
/// </summary>
public ref struct BigEndianReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _offset;

    public BigEndianReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _offset = 0;
    }

    public int Offset => _offset;
    public int Remaining => _data.Length - _offset;
    public bool HasData => _offset < _data.Length;

    public void Seek(int offset)
    {
        if (offset < 0 || offset > _data.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        _offset = offset;
    }

    public void Skip(int count)
    {
        _offset += count;
    }

    public byte ReadByte()
    {
        return _data[_offset++];
    }

    public short ReadInt16()
    {
        var value = BinaryPrimitives.ReadInt16BigEndian(_data.Slice(_offset, 2));
        _offset += 2;
        return value;
    }

    public ushort ReadUInt16()
    {
        var value = BinaryPrimitives.ReadUInt16BigEndian(_data.Slice(_offset, 2));
        _offset += 2;
        return value;
    }

    public int ReadInt32()
    {
        var value = BinaryPrimitives.ReadInt32BigEndian(_data.Slice(_offset, 4));
        _offset += 4;
        return value;
    }

    public uint ReadUInt32()
    {
        var value = BinaryPrimitives.ReadUInt32BigEndian(_data.Slice(_offset, 4));
        _offset += 4;
        return value;
    }

    public string ReadAscii(int length)
    {
        var value = Encoding.ASCII.GetString(_data.Slice(_offset, length));
        _offset += length;
        return value;
    }

    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        var slice = _data.Slice(_offset, count);
        _offset += count;
        return slice;
    }

    /// <summary>
    /// Read a big-endian Int32 at a specific offset without advancing position.
    /// </summary>
    public static int ReadInt32At(ReadOnlySpan<byte> data, int offset)
    {
        return BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset, 4));
    }

    /// <summary>
    /// Read a big-endian UInt32 at a specific offset without advancing position.
    /// </summary>
    public static uint ReadUInt32At(ReadOnlySpan<byte> data, int offset)
    {
        return BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
    }

    /// <summary>
    /// Read a big-endian Int16 at a specific offset without advancing position.
    /// </summary>
    public static short ReadInt16At(ReadOnlySpan<byte> data, int offset)
    {
        return BinaryPrimitives.ReadInt16BigEndian(data.Slice(offset, 2));
    }
}
