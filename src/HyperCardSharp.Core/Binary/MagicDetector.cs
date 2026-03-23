namespace HyperCardSharp.Core.Binary;

/// <summary>
/// Detects file format from magic bytes.
/// </summary>
public enum FileFormat
{
    Unknown,
    HyperCardStack,  // STAK at offset 4
    StuffItArchive,  // SIT! at offset 0
    DiskCopy42,      // DiskCopy 4.2 disk image
    MacBinary,       // MacBinary wrapper
    AppleSingle,     // AppleSingle (magic 0x00051600)
    AppleDouble      // AppleDouble (magic 0x00051607)
}

public static class MagicDetector
{
    /// <summary>
    /// Detect the file format from the first bytes of a file.
    /// Requires at least 8 bytes.
    /// </summary>
    public static FileFormat Detect(ReadOnlySpan<byte> header)
    {
        if (header.Length < 8)
            return FileFormat.Unknown;

        // HyperCard stack: bytes 4-7 = "STAK"
        if (header[4] == 'S' && header[5] == 'T' && header[6] == 'A' && header[7] == 'K')
            return FileFormat.HyperCardStack;

        // StuffIt archive: bytes 0-3 = "SIT!"
        if (header[0] == 'S' && header[1] == 'I' && header[2] == 'T' && header[3] == '!')
            return FileFormat.StuffItArchive;

        // AppleSingle: magic 0x00051600
        if (header[0] == 0x00 && header[1] == 0x05 && header[2] == 0x16 && header[3] == 0x00)
            return FileFormat.AppleSingle;

        // AppleDouble: magic 0x00051607
        if (header[0] == 0x00 && header[1] == 0x05 && header[2] == 0x16 && header[3] == 0x07)
            return FileFormat.AppleDouble;

        // MacBinary: byte 0 = 0, byte 74 = 0, byte 82 = 0, and name length (byte 1) is 1-63
        if (header.Length >= 128
            && header[0] == 0x00
            && header[1] >= 1 && header[1] <= 63
            && header[74] == 0x00
            && header[82] == 0x00)
            return FileFormat.MacBinary;

        // DiskCopy 4.2: byte 0 is disk name length (0-63), offset 0x52 = 0x0100 (format/magic)
        // The sample starts with "LK`" which is a name. Check for valid DiskCopy structure.
        if (header.Length >= 84)
        {
            int nameLen = header[0];
            if (nameLen >= 1 && nameLen <= 63)
            {
                // DiskCopy 4.2 has specific structure at offset 82-83
                // Format byte at offset 0x50, formatVersion at 0x51
                // Simpler heuristic: check if it has plausible data size at offset 0x40
                var dataSize = BigEndianReader.ReadInt32At(header, 0x40);
                var tagSize = BigEndianReader.ReadInt32At(header, 0x44);
                if (dataSize > 0 && dataSize < 100_000_000 && tagSize >= 0)
                    return FileFormat.DiskCopy42;
            }
        }

        return FileFormat.Unknown;
    }

    /// <summary>
    /// Returns a short format tag string from the first bytes of a file.
    /// Possible values: "STAK", "SIT!", "DCPY", "MBIN", "APLS", "APLD", "HFS!", "UNKN"
    /// </summary>
    public static string DetectFormat(ReadOnlySpan<byte> data)
    {
        return Detect(data) switch
        {
            FileFormat.HyperCardStack => "STAK",
            FileFormat.StuffItArchive => "SIT!",
            FileFormat.DiskCopy42     => "DCPY",
            FileFormat.MacBinary      => "MBIN",
            FileFormat.AppleSingle    => "APLS",
            FileFormat.AppleDouble    => "APLD",
            _                         => DetectHfs(data)
        };
    }

    private static string DetectHfs(ReadOnlySpan<byte> data)
    {
        // HFS MDB signature 0xD2D7 at block 2 (offset 1024)
        if (data.Length >= 1026)
        {
            ushort sig = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(data.Slice(1024, 2));
            if (sig == 0xD2D7)
                return "HFS!";
        }
        return "UNKN";
    }
}
