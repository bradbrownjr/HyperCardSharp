using System.Buffers.Binary;

namespace HyperCardSharp.Core.Containers;

/// <summary>
/// Extracts STAK files from an HFS or HFS+ filesystem image.
/// The input is expected to be a raw HFS/HFS+ volume (e.g., extracted from a DiskCopy image).
/// </summary>
public class HfsExtractor : IContainerExtractor
{
    private const ushort HfsMdbSignature = 0xD2D7;
    private const ushort HfsPlusSignature = 0x482B; // "H+"
    private const int MdbOffset = 1024; // Block 2 * 512 bytes

    public bool CanHandle(ReadOnlySpan<byte> data)
    {
        if (data.Length < MdbOffset + 2)
            return false;

        ushort sig = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(MdbOffset, 2));
        return sig == HfsMdbSignature || sig == HfsPlusSignature;
    }

    public byte[]? Extract(byte[] data)
    {
        if (!CanHandle(data))
            return null;

        try
        {
            // Check for HFS+ first
            if (data.Length >= MdbOffset + 4)
            {
                ushort sig = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(MdbOffset, 2));
                if (sig == HfsPlusSignature)
                {
                    var hfsPlusReader = new HfsPlusReader(data);
                    if (hfsPlusReader.IsHfsPlus())
                    {
                        var stacks = hfsPlusReader.EnumerateStacks();
                        if (stacks.Count > 0)
                            return stacks[0].Data;
                    }
                    return null;
                }
            }

            // Fall back to classic HFS
            var reader = new HfsReader(data);
            return reader.ExtractFirstStack();
        }
        catch
        {
            return null;
        }
    }
}
