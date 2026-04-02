using System.Buffers.Binary;

namespace HyperCardSharp.Core.Resources;

/// <summary>
/// Decodes Mac 'snd ' resources into standard RIFF/WAV bytes for cross-platform playback.
///
/// Supports:
///   Format 1  — sndListResource (numModifiers synths, then commands)
///   Format 2  — sndResource     (refCount, then commands)
///   bufferCmd (0x8051) — offset to a SoundHeader within the resource
///   SoundHeader encode=0x00 (standard): 8-bit unsigned mono PCM
///   SoundHeader encode=0xFF (extended): 8-bit or 16-bit PCM, mono or stereo
///
/// Compressed sound (encode=0xFE, MACE/IMA) is not supported; returns null.
///
/// Reference: Inside Macintosh: Sound, Chapter 2.
/// TODO: verify byte offsets against real stack snd resources.
/// </summary>
public static class SoundDecoder
{
    private const byte EncodeStandard   = 0x00;
    private const byte EncodeExtended   = 0xFF;
    private const byte EncodeCompressed = 0xFE;

    private const int SoundHeaderSize = 22; // bytes before sample data in standard header

    /// <summary>
    /// Attempt to decode a Mac 'snd ' resource to WAV bytes.
    /// Returns null if the format is unrecognized, compressed, or the data is malformed.
    /// </summary>
    public static byte[]? Decode(byte[] resourceData)
    {
        if (resourceData == null || resourceData.Length < 6)
            return null;

        try
        {
            return DecodeCore(resourceData);
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? DecodeCore(byte[] data)
    {
        var span = data.AsSpan();
        short format = BinaryPrimitives.ReadInt16BigEndian(span.Slice(0, 2));

        int cmdListOffset;
        if (format == 1)
        {
            // Format 1: 2-byte format, 2-byte numModifiers, then modifier records (6 bytes each)
            short numModifiers = BinaryPrimitives.ReadInt16BigEndian(span.Slice(2, 2));
            cmdListOffset = 4 + numModifiers * 6;
        }
        else if (format == 2)
        {
            // Format 2: 2-byte format, 2-byte refCount
            cmdListOffset = 4;
        }
        else
        {
            return null;
        }

        if (cmdListOffset + 2 > data.Length)
            return null;

        short numCmds = BinaryPrimitives.ReadInt16BigEndian(span.Slice(cmdListOffset, 2));
        if (numCmds <= 0 || numCmds > 256)
            return null;

        int cmdBase = cmdListOffset + 2;

        // Scan commands for bufferCmd (0x8051) which points to the SoundHeader
        for (int i = 0; i < numCmds; i++)
        {
            int cmdOffset = cmdBase + i * 8;
            if (cmdOffset + 8 > data.Length)
                break;

            ushort cmd    = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(cmdOffset, 2));
            int    param2 = BinaryPrimitives.ReadInt32BigEndian(span.Slice(cmdOffset + 4, 4));

            // bufferCmd = 0x8051; soundCmd with data-offset flag also = 0x8051
            if ((cmd & 0x7FFF) == 0x0051 && (cmd & 0x8000) != 0)
            {
                // param2 is absolute offset from start of resource data to SoundHeader
                return DecodeSoundHeader(data, param2);
            }
        }

        return null;
    }

    private static byte[]? DecodeSoundHeader(byte[] data, int headerOffset)
    {
        if (headerOffset < 0 || headerOffset + SoundHeaderSize > data.Length)
            return null;

        var span = data.AsSpan();

        // SoundHeader layout (big-endian):
        //  +0   uint32  samplePtr  (0 in resource; samples follow header)
        //  +4   uint32  numChannels
        //  +8   Fixed   sampleRate (16.16 fixed-point)
        //  +12  uint32  loopStart
        //  +16  uint32  loopEnd
        //  +20  uint8   encode
        //  +21  uint8   baseFrequency
        //  [+22 start of samples for encode=0x00]
        //  [+22 uint32 numSamples, then samples for encode=0xFF]

        uint   numChannels = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(headerOffset + 4, 4));
        uint   sampleRateFixed = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(headerOffset + 8, 4));
        byte   encode      = span[headerOffset + 20];

        if (numChannels == 0 || numChannels > 2)
            numChannels = 1;

        // Convert 16.16 fixed-point sample rate to integer Hz
        int sampleRate = (int)(sampleRateFixed >> 16);
        if (sampleRate < 1000 || sampleRate > 96000)
            sampleRate = 22254; // Mac standard fallback

        byte[]? samples;
        int bitsPerSample;

        if (encode == EncodeStandard)
        {
            // Standard header: encode=0, 8-bit samples follow at +22
            int samplesStart = headerOffset + SoundHeaderSize;
            int numSamples   = data.Length - samplesStart;
            if (numSamples <= 0)
                return null;
            samples = data[samplesStart..];
            bitsPerSample = 8;
        }
        else if (encode == EncodeExtended)
        {
            // Extended header: +22 = numFrames (uint32), +26 = AIFFSampleRate (10 bytes,
            // 80-bit extended float), +36..+47 = markers/instruments/AES,
            // +48 = sampleSize (uint16), +50..+55 = futureUse (6 bytes), +56 = samples
            if (headerOffset + 56 > data.Length)
                return null;

            uint   numFrames  = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(headerOffset + 22, 4));
            ushort sampleSize = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(headerOffset + 48, 2));

            // Try to read AIFF 80-bit sample rate (convert to integer)
            int aiffRate = Read80BitRate(span.Slice(headerOffset + 26, 10));
            if (aiffRate >= 1000)
                sampleRate = aiffRate;

            bitsPerSample = sampleSize is 8 or 16 ? sampleSize : 8;

            int samplesStart = headerOffset + 56;
            int bytesPerSample = bitsPerSample / 8;
            int expectedBytes  = (int)(numFrames * numChannels * (uint)bytesPerSample);
            if (expectedBytes <= 0 || samplesStart + expectedBytes > data.Length)
                expectedBytes = data.Length - samplesStart;
            if (expectedBytes <= 0)
                return null;

            samples = data[samplesStart..(samplesStart + expectedBytes)];

            // 16-bit extended header samples are big-endian signed; WAV expects little-endian
            if (bitsPerSample == 16)
                samples = ConvertBigEndian16ToLittleEndian(samples);
        }
        else
        {
            // Compressed (encode=0xFE) — not supported
            return null;
        }

        return BuildWav(samples, (int)numChannels, sampleRate, bitsPerSample);
    }

    /// <summary>
    /// Build a RIFF/WAV byte array from raw PCM sample data.
    /// 8-bit WAV uses unsigned bytes (0x80 = silence), same as Mac snd standard.
    /// 16-bit WAV uses signed little-endian int16.
    /// </summary>
    public static byte[] BuildWav(byte[] pcm, int numChannels, int sampleRate, int bitsPerSample)
    {
        int byteRate    = sampleRate * numChannels * (bitsPerSample / 8);
        int blockAlign  = numChannels * (bitsPerSample / 8);
        int dataSize    = pcm.Length;
        int totalSize   = 36 + dataSize;

        var wav = new byte[8 + totalSize];
        var span = wav.AsSpan();

        // RIFF header
        span[0] = (byte)'R'; span[1] = (byte)'I'; span[2] = (byte)'F'; span[3] = (byte)'F';
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4, 4), totalSize);
        span[8]  = (byte)'W'; span[9]  = (byte)'A'; span[10] = (byte)'V'; span[11] = (byte)'E';

        // fmt chunk
        span[12] = (byte)'f'; span[13] = (byte)'m'; span[14] = (byte)'t'; span[15] = (byte)' ';
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(16, 4), 16);   // chunk size
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(20, 2), 1);    // PCM
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(22, 2), (short)numChannels);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(24, 4), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(28, 4), byteRate);
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(32, 2), (short)blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(34, 2), (short)bitsPerSample);

        // data chunk
        span[36] = (byte)'d'; span[37] = (byte)'a'; span[38] = (byte)'t'; span[39] = (byte)'a';
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(40, 4), dataSize);
        pcm.CopyTo(span.Slice(44));

        return wav;
    }

    /// <summary>
    /// Convert 80-bit IEEE 754 extended (AIFF) to integer Hz.
    /// Layout: 1 sign + 15 exponent bits, then 64-bit explicit mantissa (big-endian).
    /// </summary>
    private static int Read80BitRate(ReadOnlySpan<byte> tenBytes)
    {
        try
        {
            int exp = ((tenBytes[0] & 0x7F) << 8) | tenBytes[1];
            ulong mantissa = BinaryPrimitives.ReadUInt64BigEndian(tenBytes.Slice(2, 8));
            double value = mantissa / (double)(1UL << 63) * Math.Pow(2.0, exp - 16383);
            int rate = (int)Math.Round(value);
            return (rate >= 1000 && rate <= 192000) ? rate : 0;
        }
        catch { return 0; }
    }

    private static byte[] ConvertBigEndian16ToLittleEndian(byte[] src)
    {
        var dst = new byte[src.Length];
        for (int i = 0; i + 1 < src.Length; i += 2)
        {
            dst[i]     = src[i + 1];
            dst[i + 1] = src[i];
        }
        return dst;
    }
}
